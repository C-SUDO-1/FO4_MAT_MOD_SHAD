
using RenderArchitectureDB.Model;
using RenderArchitectureDB.Util;
using System.Text.Json;

namespace RenderArchitectureDB.Commands;

public static class ExportCommand
{
    public static int Run(string[] args)
    {
        var inDir = Args.GetRequired(args, "--in");
        var outDir = Args.GetRequired(args, "--out");

        Directory.CreateDirectory(Path.Combine(outDir, "exports"));

        // 1) perm↔shader links (if present)
        var shaderLinksPath = Path.Combine(inDir, "correlation", "perm_shader_links.json");
        if (File.Exists(shaderLinksPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(shaderLinksPath));
            var linksEl = doc.RootElement.GetProperty("links");
            var links = JsonSerializer.Deserialize<List<PermShaderLink>>(linksEl.GetRawText(), StableJson.Options) ?? new();

            var csvPath = Path.Combine(outDir, "exports", "perm_shader_links.csv");
            using (var sw = new StreamWriter(csvPath))
            {
                sw.WriteLine("perm_id,shader_id,stage,evidence_count,confidence_score,mapping_type");
                foreach (var l in links.OrderByDescending(x => x.ConfidenceScore).ThenByDescending(x => x.EvidenceCount))
                    sw.WriteLine($"{l.PermId},{l.ShaderId},{l.Stage},{l.EvidenceCount},{l.ConfidenceScore:F3},{l.MappingType}");
            }
            Console.WriteLine($"[Export] wrote {csvPath}");
        }
        else
        {
            Console.WriteLine("[Export] No correlation/perm_shader_links.json found. Run link first (or after census).");
        }

        // 2) perm↔material links (if present)
        var matLinksPath = Path.Combine(inDir, "correlation", "perm_material_links.json");
        if (File.Exists(matLinksPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(matLinksPath));
            var linksEl = doc.RootElement.GetProperty("links");
            var links = JsonSerializer.Deserialize<List<PermMaterialLink>>(linksEl.GetRawText(), StableJson.Options) ?? new();

            var csvPath = Path.Combine(outDir, "exports", "perm_material_links.csv");
            using (var sw = new StreamWriter(csvPath))
            {
                sw.WriteLine("perm_id,block_type,shader_type,spf1,spf2,material_path,hit_count,unique_nif_count");
                foreach (var l in links.OrderByDescending(x => x.HitCount).ThenByDescending(x => x.UniqueNifCount))
                {
                    var matEsc = (l.MaterialPath ?? string.Empty).Replace("\"", "\"\"");
                    sw.WriteLine($"{l.PermId},{l.BlockType},{l.ShaderType},{l.Spf1},{l.Spf2},\"{matEsc}\",{l.HitCount},{l.UniqueNifCount}");
                }
            }
            Console.WriteLine($"[Export] wrote {csvPath}");
        }
        else
        {
            Console.WriteLine("[Export] No correlation/perm_material_links.json found. Run link-materials first.");
        }

        return 0;
    }
}

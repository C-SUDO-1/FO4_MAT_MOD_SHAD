using RenderArchitectureDB.Model;
using RenderArchitectureDB.Services;
using RenderArchitectureDB.Util;
using System.Text.Json;

namespace RenderArchitectureDB.Commands;

public static class ClusterCommand
{
    public static int Run(string[] args)
    {
        var inDir = Args.GetRequired(args, "--in");
        var outDir = Args.GetRequired(args, "--out");

        var shaderTablePath = Path.Combine(inDir, "normalized", "shader_table.json");
        if (!File.Exists(shaderTablePath))
            throw new FileNotFoundException("Missing normalized/shader_table.json. Run normalize first.", shaderTablePath);

        // shader_table.json can be either:
        //  - { "schema_version": "...", "shaders": [ ... ] }
        //  - [ ... ]   (older/perm-only outputs)
        //  - []        (perm-only mode; no shaders)
        List<ShaderRecord> shaders = new();

        var jsonText = File.ReadAllText(shaderTablePath);
        if (!string.IsNullOrWhiteSpace(jsonText))
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("shaders", out var shadersEl) && shadersEl.ValueKind == JsonValueKind.Array)
                {
                    shaders = JsonSerializer.Deserialize<List<ShaderRecord>>(shadersEl.GetRawText(), StableJson.Options)
                              ?? new List<ShaderRecord>();
                }
                else
                {
                    shaders = new List<ShaderRecord>();
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                shaders = JsonSerializer.Deserialize<List<ShaderRecord>>(root.GetRawText(), StableJson.Options)
                          ?? new List<ShaderRecord>();
            }
            else
            {
                shaders = new List<ShaderRecord>();
            }
        }

        var clusters = ClusterBuilder.BuildClusters(shaders);

        Directory.CreateDirectory(Path.Combine(outDir, "normalized"));

        StableJson.WriteToFile(Path.Combine(outDir, "normalized", "structural_clusters.json"),
            new { schema_version = SchemaConstants.SchemaVersion, clusters });

        Console.WriteLine($"[Cluster] clusters={clusters.Count}");
        return 0;
    }
}

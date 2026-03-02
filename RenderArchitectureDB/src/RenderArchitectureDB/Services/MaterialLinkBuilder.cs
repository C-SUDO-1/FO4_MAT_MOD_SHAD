
using CsvHelper;
using CsvHelper.Configuration;
using RenderArchitectureDB.Model;
using RenderArchitectureDB.Util;
using System.Globalization;

namespace RenderArchitectureDB.Services;

public static class MaterialLinkBuilder
{
    public static List<PermMaterialLink> BuildPermMaterialLinks(string mapCsvPath)
    {
        if (!File.Exists(mapCsvPath))
            throw new FileNotFoundException("Map CSV not found", mapCsvPath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
            TrimOptions = TrimOptions.Trim,
            PrepareHeaderForMatch = args => NormalizeHeader(args.Header)
        };

        using var reader = new StreamReader(mapCsvPath);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        var headers = (csv.HeaderRecord ?? Array.Empty<string>())
            .Select(NormalizeHeader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Hard validate expected headers, because returning 0 links is confusing.
        string[] required = { "NifFile", "BlockType", "ShaderType", "SPF1_Raw", "SPF2_Raw", "MaterialPath" };
        var missing = required.Where(h => !headers.Contains(h)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException("Map CSV missing required headers: " + string.Join(", ", missing));

        var rows = new List<Row>();
        int totalRows = 0;
        int skippedEmptyMat = 0;

        while (csv.Read())
        {
            totalRows++;

            var nif = csv.GetField("NifFile")?.Trim() ?? "";
            var block = csv.GetField("BlockType")?.Trim() ?? "";
            var shaderRaw = csv.GetField("ShaderType")?.Trim() ?? "";
            var spf1 = ParseUInt(csv.GetField("SPF1_Raw"));
            var spf2 = ParseUInt(csv.GetField("SPF2_Raw"));
            var mat = csv.GetField("MaterialPath")?.Trim() ?? "";

            // Treat "null"/"None" as empty
            if (string.Equals(mat, "null", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mat, "none", StringComparison.OrdinalIgnoreCase))
                mat = "";

            if (string.IsNullOrWhiteSpace(mat))
            {
                skippedEmptyMat++;
                continue;
            }

            int shaderType = 0;
            if (!string.IsNullOrWhiteSpace(shaderRaw) &&
                !string.Equals(shaderRaw, "Default", StringComparison.OrdinalIgnoreCase))
                _ = int.TryParse(shaderRaw, out shaderType);

            var permKey = $"{block}|{shaderType}|{spf1}|{spf2}";
            var permId = Crypto.Sha1Hex(permKey);

            rows.Add(new Row
            {
                NifFile = nif,
                BlockType = block,
                ShaderType = shaderType,
                Spf1 = spf1,
                Spf2 = spf2,
                MaterialPath = mat,
                PermId = permId
            });
        }

        Console.WriteLine($"[LinkMaterials] map rows={totalRows}, rows_with_material={rows.Count}, skipped_empty_material={skippedEmptyMat}");

        var links = rows
            .GroupBy(r => new { r.PermId, r.BlockType, r.ShaderType, r.Spf1, r.Spf2, r.MaterialPath })
            .Select(g =>
            {
                var uniqueNifs = g.Select(x => x.NifFile)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                return new PermMaterialLink(
                    PermId: g.Key.PermId,
                    BlockType: g.Key.BlockType,
                    ShaderType: g.Key.ShaderType,
                    Spf1: g.Key.Spf1,
                    Spf2: g.Key.Spf2,
                    MaterialPath: g.Key.MaterialPath,
                    HitCount: g.Count(),
                    UniqueNifCount: uniqueNifs
                );
            })
            .OrderByDescending(l => l.HitCount)
            .ThenByDescending(l => l.UniqueNifCount)
            .ThenBy(l => l.PermId, StringComparer.Ordinal)
            .ThenBy(l => l.MaterialPath, StringComparer.Ordinal)
            .ToList();

        return links;
    }

    private sealed class Row
    {
        public string NifFile { get; set; } = "";
        public string BlockType { get; set; } = "";
        public int ShaderType { get; set; }
        public uint Spf1 { get; set; }
        public uint Spf2 { get; set; }
        public string MaterialPath { get; set; } = "";
        public string PermId { get; set; } = "";
    }

    private static string NormalizeHeader(string? h)
        => string.IsNullOrEmpty(h) ? "" : h.Replace("\ufeff", "").Trim().Trim('"');

    private static uint ParseUInt(string? s)
    {
        s = (s ?? "").Trim();
        if (uint.TryParse(s, out var v)) return v;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hv))
            return hv;
        return 0;
    }
}

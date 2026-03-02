using CsvHelper;
using CsvHelper.Configuration;
using RenderArchitectureDB.Model;
using RenderArchitectureDB.Util;
using System.Globalization;

namespace RenderArchitectureDB.Services;

public static class PermutationImporter
{
    /// <summary>
    /// Reads permutation index CSVs produced by FO4 permutation tooling.
    ///
    /// Required semantic fields:
    ///   BlockType, ShaderType, SPF1, SPF2
    ///
    /// This importer is tolerant:
    /// - ShaderType may be "Default" (string) -> normalized to 0 for now.
    /// - SPF1/SPF2 may appear as SPF1_int/SPF2_int; we prefer *_int when present.
    /// - OccurrenceCount may be Occurrences or Count.
    /// - DominantClass may be DominantClass or Dominant_Class.
    /// - Handles BOM in headers.
    /// </summary>
    public static List<PermutationRecord> Load(string permCsvPath)
    {
        if (!File.Exists(permCsvPath))
            throw new FileNotFoundException("Permutation CSV not found", permCsvPath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
            TrimOptions = TrimOptions.Trim,
            PrepareHeaderForMatch = args => NormalizeHeader(args.Header)
        };

        using var reader = new StreamReader(permCsvPath);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        var headers = (csv.HeaderRecord ?? Array.Empty<string>())
            .Select(NormalizeHeader)
            .ToArray();

        bool Has(string h) => headers.Any(x => string.Equals(x, NormalizeHeader(h), StringComparison.OrdinalIgnoreCase));

        if (!Has("BlockType") || !Has("ShaderType"))
            throw new InvalidOperationException("Permutation CSV missing required headers: BlockType, ShaderType");

        if (!(Has("SPF1") || Has("SPF1_int")) || !(Has("SPF2") || Has("SPF2_int")))
            throw new InvalidOperationException("Permutation CSV missing required headers: SPF1/SPF1_int and SPF2/SPF2_int");

        string Pick(params string[] candidates)
        {
            foreach (var c in candidates)
            {
                var cn = NormalizeHeader(c);
                var match = headers.FirstOrDefault(h => string.Equals(h, cn, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return NormalizeHeader(candidates[0]);
        }

        var hBlock = Pick("BlockType");
        var hShaderType = Pick("ShaderType");
        var hSpf1 = Has("SPF1_int") ? Pick("SPF1_int") : Pick("SPF1");
        var hSpf2 = Has("SPF2_int") ? Pick("SPF2_int") : Pick("SPF2");

        var hOcc = (Has("OccurrenceCount") || Has("Occurrences") || Has("Count"))
            ? Pick("OccurrenceCount", "Occurrences", "Count")
            : "";

        var hDom = (Has("DominantClass") || Has("Dominant_Class"))
            ? Pick("DominantClass", "Dominant_Class")
            : "";

        var records = new List<PermutationRecord>();

        while (csv.Read())
        {
            var blockType = GetString(csv, hBlock);
            var shaderTypeRaw = GetString(csv, hShaderType);

            // Normalize ShaderType:
            // - If numeric, use it.
            // - If "Default" or empty -> 0 (v1.0).
            // Future: map FO4 enum strings to numeric.
            int shaderType = 0;
            if (!string.IsNullOrWhiteSpace(shaderTypeRaw) &&
                !string.Equals(shaderTypeRaw, "Default", StringComparison.OrdinalIgnoreCase))
            {
                _ = int.TryParse(shaderTypeRaw, out shaderType);
            }

            uint spf1 = GetUInt(csv, hSpf1);
            uint spf2 = GetUInt(csv, hSpf2);

            int occ = 0;
            if (!string.IsNullOrWhiteSpace(hOcc))
                _ = int.TryParse(GetString(csv, hOcc), out occ);

            string? dom = null;
            if (!string.IsNullOrWhiteSpace(hDom))
            {
                dom = GetString(csv, hDom);
                if (string.IsNullOrWhiteSpace(dom)) dom = null;
            }

            var permKey = $"{blockType}|{shaderType}|{spf1}|{spf2}";
            var permId = Crypto.Sha1Hex(permKey);

            records.Add(new PermutationRecord(
                PermId: permId,
                BlockType: blockType,
                ShaderType: shaderType,
                Spf1: spf1,
                Spf2: spf2,
                DominantClass: dom,
                OccurrenceCount: occ
            ));
        }

        return records
            .OrderBy(r => r.BlockType, StringComparer.Ordinal)
            .ThenBy(r => r.ShaderType)
            .ThenBy(r => r.Spf1)
            .ThenBy(r => r.Spf2)
            .ToList();
    }

    private static string NormalizeHeader(string? h)
        => string.IsNullOrEmpty(h) ? "" : h.Replace("\ufeff", "").Trim().Trim('"');

    private static string GetString(CsvReader csv, string header)
    {
        try { return (csv.GetField(header) ?? "").Trim(); }
        catch { return ""; }
    }

    private static uint GetUInt(CsvReader csv, string header)
    {
        var s = GetString(csv, header);
        if (uint.TryParse(s, out var v)) return v;
        // allow hex like 0x80400201
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hv))
            return hv;
        return 0;
    }
}

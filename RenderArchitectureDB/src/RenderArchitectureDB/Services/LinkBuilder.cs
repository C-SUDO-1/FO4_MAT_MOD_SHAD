
using RenderArchitectureDB.Model;

namespace RenderArchitectureDB.Services;

public static class LinkBuilder
{
    /// <summary>
    /// Phase 1 linking strategy (v1.0): DIRECT OBSERVATION ONLY.
    /// We do not infer perm_id from sessions here because we need the observation stream to include perm_id.
    ///
    /// Therefore, this builder expects a file at:
    ///   <outDir>/raw/observations/perm_shader_observations.jsonl
    /// Each line:
    /// { "perm_id":"...", "shader_id":"ps_...", "stage":"PS", "timestamp": 123 }
    ///
    /// If you don't have perm_id in runtime yet, you can still produce shader_table and clusters,
    /// but perm_shader_links will be empty until observation capture exists.
    /// </summary>
    public static List<PermShaderLink> BuildFromObservationJsonl(string observationJsonlPath)
    {
        if (!File.Exists(observationJsonlPath))
            return new List<PermShaderLink>();

        var map = new Dictionary<(string permId, string shaderId, string stage), int>();

        foreach (var line in File.ReadLines(observationJsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = System.Text.Json.JsonDocument.Parse(line);
            var root = doc.RootElement;

            var permId = root.GetProperty("perm_id").GetString() ?? "";
            var shaderId = root.GetProperty("shader_id").GetString() ?? "";
            var stage = root.GetProperty("stage").GetString() ?? "";

            if (string.IsNullOrWhiteSpace(permId) || string.IsNullOrWhiteSpace(shaderId) || string.IsNullOrWhiteSpace(stage))
                continue;

            var key = (permId, shaderId, stage);
            map[key] = map.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        // confidence: v1.0 = min(1, evidence/10)
        var links = map
            .Select(kvp =>
            {
                var (permId, shaderId, stage) = kvp.Key;
                var evidence = kvp.Value;
                var conf = Math.Min(1.0, evidence / 10.0);
                return new PermShaderLink(permId, shaderId, stage, evidence, conf, "DirectObservation");
            })
            .OrderByDescending(l => l.ConfidenceScore)
            .ThenByDescending(l => l.EvidenceCount)
            .ThenBy(l => l.PermId, StringComparer.Ordinal)
            .ThenBy(l => l.Stage, StringComparer.Ordinal)
            .ThenBy(l => l.ShaderId, StringComparer.Ordinal)
            .ToList();

        return links;
    }
}

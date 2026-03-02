
using RenderArchitectureDB.Model;
using System.Text.Json;

namespace RenderArchitectureDB.Services;

public static class SessionImporter
{
    /// <summary>
    /// Reads FO4ShaderDB/sessions/*.json. We support two formats:
    ///  A) { "session_name": "...", "observations": [ { "shader_id": "...", "stage": "PS", "timestamp": 123 } ] }
    ///  B) JSONL (one observation per line): { "shader_id": "...", "stage": "PS", "timestamp": 123 }
    /// Adjust here if your census uses a different layout.
    /// </summary>
    public static List<SessionObservation> Load(string fo4ShaderDbDir)
    {
        var sessionsDir = Path.Combine(fo4ShaderDbDir, "sessions");
        if (!Directory.Exists(sessionsDir))
            return new List<SessionObservation>();

        var obs = new List<SessionObservation>();

        foreach (var path in Directory.GetFiles(sessionsDir, "*.*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".json")
            {
                var text = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (root.TryGetProperty("observations", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var sessionName = root.TryGetProperty("session_name", out var sn) ? (sn.GetString() ?? Path.GetFileNameWithoutExtension(path)) : Path.GetFileNameWithoutExtension(path);
                    foreach (var item in arr.EnumerateArray())
                    {
                        var shaderId = item.GetProperty("shader_id").GetString() ?? "";
                        var stage = item.GetProperty("stage").GetString() ?? "";
                        var ts = item.TryGetProperty("timestamp", out var tv) ? tv.GetInt64() : 0;
                        obs.Add(new SessionObservation(sessionName, stage, shaderId, ts));
                    }
                }
            }
            else if (ext == ".jsonl")
            {
                var sessionName = Path.GetFileNameWithoutExtension(path);
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var shaderId = root.GetProperty("shader_id").GetString() ?? "";
                    var stage = root.GetProperty("stage").GetString() ?? "";
                    var ts = root.TryGetProperty("timestamp", out var tv) ? tv.GetInt64() : 0;
                    obs.Add(new SessionObservation(sessionName, stage, shaderId, ts));
                }
            }
        }

        return obs
            .OrderBy(o => o.SessionName, StringComparer.Ordinal)
            .ThenBy(o => o.Stage, StringComparer.Ordinal)
            .ThenBy(o => o.ShaderId, StringComparer.Ordinal)
            .ThenBy(o => o.FirstSeenUnixMs)
            .ToList();
    }
}

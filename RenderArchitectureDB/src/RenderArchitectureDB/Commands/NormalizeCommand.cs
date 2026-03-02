using RenderArchitectureDB.Model;
using RenderArchitectureDB.Services;
using RenderArchitectureDB.Util;

namespace RenderArchitectureDB.Commands;

public static class NormalizeCommand
{
    // Returns process exit code (0 = success).
    public static int Run(string[] args)
    {
        var permCsv = Args.GetRequired(args, "--permCsv");
        var outDir  = Args.GetRequired(args, "--out");

        // shaderDb is OPTIONAL (perm-only mode supported)
        var shaderDbDir = Args.GetOptional(args, "--shaderDb") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(permCsv) || !File.Exists(permCsv))
            throw new FileNotFoundException("Permutation CSV not found", permCsv);

        Directory.CreateDirectory(outDir);
        var normalizedDir = Path.Combine(outDir, "normalized");
        Directory.CreateDirectory(normalizedDir);

        // 1) Load permutation table (required)
        // (These loaders are static in this repo.)
        var perms = PermutationImporter.Load(permCsv);

        // 2) Load shader/session tables (optional)
        var shaders  = new List<ShaderRecord>();
        var sessions = new List<SessionObservation>();

        if (!string.IsNullOrWhiteSpace(shaderDbDir) && Directory.Exists(shaderDbDir))
        {
            shaders  = ShaderDbImporter.Load(shaderDbDir);
            sessions = SessionImporter.Load(shaderDbDir);
        }
        else
        {
            var shown = string.IsNullOrWhiteSpace(shaderDbDir) ? "(not provided)" : shaderDbDir;
            Console.WriteLine($"[Normalize] ShaderDB directory not found: {shown}");
            Console.WriteLine("[Normalize] Continuing in perm-only mode (shader/session tables will be empty).");
        }

        // 3) Emit stable-sorted JSON tables
        StableJson.WriteToFile(Path.Combine(normalizedDir, "perm_table.json"), perms);
        StableJson.WriteToFile(Path.Combine(normalizedDir, "shader_table.json"), shaders);
        StableJson.WriteToFile(Path.Combine(normalizedDir, "session_table.json"), sessions);

        // 4) Emit an empty structural clusters file for determinism / downstream steps
        var structuralClustersPath = Path.Combine(normalizedDir, "structural_clusters.json");
        if (!File.Exists(structuralClustersPath))
            StableJson.WriteToFile(structuralClustersPath, new List<object>());

        Console.WriteLine($"[Normalize] perms={perms.Count}, shaders={shaders.Count}, sessions={sessions.Count}");
        return 0;
    }
}

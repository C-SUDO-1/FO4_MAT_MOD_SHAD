using RenderArchitectureDB.Services;
using RenderArchitectureDB.Util;

namespace RenderArchitectureDB.Commands;

public static class LinkMaterialsCommand
{
    public static int Run(string[] args)
    {
        var inDir = Args.GetRequired(args, "--in");
        var mapCsv = Args.GetRequired(args, "--mapCsv");
        var outDir = Args.GetRequired(args, "--out");

        // Ensure normalize ran (perm_table exists) - we can still link without it, but it is a useful guard.
        var permTablePath = Path.Combine(inDir, "normalized", "perm_table.json");
        if (!File.Exists(permTablePath))
            Console.WriteLine($"[LinkMaterials] WARNING: missing {permTablePath} (did you run normalize?). Continuing anyway.");

        var links = MaterialLinkBuilder.BuildPermMaterialLinks(mapCsv);

        StableJson.WriteToFile(Path.Combine(outDir, "correlation", "perm_material_links.json"),
            new { schema_version = "1.0", correlation_model_version = "1.0", links });

        Console.WriteLine($"[LinkMaterials] links={links.Count} (from nif_material_permutation_map.csv)");
        return 0;
    }
}

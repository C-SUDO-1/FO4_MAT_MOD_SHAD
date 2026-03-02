
using RenderArchitectureDB.Services;
using RenderArchitectureDB.Util;

namespace RenderArchitectureDB.Commands;

public static class LinkCommand
{
    public static int Run(string[] args)
    {
        var inDir = Args.GetRequired(args, "--in");
        var outDir = Args.GetRequired(args, "--out");

        var obsPath = Path.Combine(inDir, "raw", "observations", "perm_shader_observations.jsonl");
        var links = LinkBuilder.BuildFromObservationJsonl(obsPath);

        StableJson.WriteToFile(Path.Combine(outDir, "correlation", "perm_shader_links.json"),
            new { schema_version = "1.0", correlation_model_version = "1.0", links });

        Console.WriteLine($"[Link] links={links.Count} (needs perm_shader_observations.jsonl)");
        return 0;
    }
}

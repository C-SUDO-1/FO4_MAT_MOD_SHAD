using RenderArchitectureDB.Commands;

namespace RenderArchitectureDB;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            CliHelp.Print();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        try
        {
            return cmd switch
            {
                "normalize"       => NormalizeCommand.Run(rest),
                "cluster"         => ClusterCommand.Run(rest),
                "link"            => LinkCommand.Run(rest),
                "link-materials"  => LinkMaterialsCommand.Run(rest),
                "export"          => ExportCommand.Run(rest),
                _ => CliHelp.Unknown(cmd)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[RenderArchitectureDB] ERROR: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 2;
        }
    }
}

namespace RenderArchitectureDB.Commands;

public static class CliHelp
{
    public static void Print()
    {
        Console.WriteLine("RenderArchitectureDB (C#) — v1.0");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  normalize       --permCsv <permutation_index.csv> --shaderDb <FO4ShaderDB> --out <outDir>");
        Console.WriteLine("  cluster         --in <outDir> --out <outDir>");
        Console.WriteLine("  link            --in <outDir> --out <outDir>");
        Console.WriteLine("  link-materials  --in <outDir> --mapCsv <nif_material_permutation_map.csv> --out <outDir>");
        Console.WriteLine("  export          --in <outDir> --out <outDir>");
        Console.WriteLine();
        Console.WriteLine("Examples (copy/paste safe):");
        Console.WriteLine(@"  dotnet run --project .\src\RenderArchitectureDB -- normalize --permCsv .\permutation_index_STRICT_decoded_assetclass.csv --shaderDb ..\FO4ShaderDB --out .\radb_out");
        Console.WriteLine(@"  dotnet run --project .\src\RenderArchitectureDB -- link-materials --in .\radb_out --mapCsv .\nif_material_permutation_map.csv --out .\radb_out");
        Console.WriteLine(@"  dotnet run --project .\src\RenderArchitectureDB -- export --in .\radb_out --out .\radb_out");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - This tool is OFFLINE. It reads your Permutation Index CSV and (optionally) FO4ShaderCensus ShaderDB.");
        Console.WriteLine("  - Outputs are stable-sorted for deterministic diffs.");
    }

    public static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        Print();
        return 1;
    }
}

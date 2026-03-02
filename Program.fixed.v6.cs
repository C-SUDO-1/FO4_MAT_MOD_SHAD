using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MaterialLib;

namespace MaterialCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || HasArg(args, "--help") || HasArg(args, "-h"))
            {
                PrintHelp();
                return 0;
            }

            var cmd = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();

            return cmd switch
            {
                "list"   => CmdList(rest),
                "set"    => CmdSet(rest),
                "schema" => CmdSchema(rest),
                "scan"   => CmdScan(rest),
                "sweep"  => CmdSweep(rest), // still stubbed
                _        => Unknown(cmd)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        var exe = Path.GetFileName(Environment.ProcessPath ?? "MaterialCli");

        Console.WriteLine($@"{exe} - Fallout 4 material CLI

USAGE
  {exe} --help
  {exe} list   --in <file.bgsm|file.bgem>
  {exe} set    --in <file.bgsm|file.bgem> --out <file> --param ""Name=Value"" [--param ""Name=Value"" ...]
  {exe} schema --out <file.json>
  {exe} scan   --root <dir> --out <file.json> [--threads <N>] [--include-defaults] [--textures] [--limit <N>]
  {exe} sweep  --config <file.json>

NOTES
  - Aliases (BGSM):
      Roughness -> Smoothness (stored as 1 - Roughness)
      Specular  -> SpecularMult

EXAMPLES
  {exe} list   --in ""C:\...\Alien_Body.BGSM""
  {exe} set    --in ""Alien_Body.BGSM"" --out ""Alien_Body_edited.BGSM"" --param ""Roughness=0.65"" --param ""Specular=0.2""
  {exe} schema --out ""schema.json""
  {exe} scan   --root ""C:\Program Files (x86)\Steam\steamapps\common\Fallout 4\Data\Materials"" --out ""materials_scan.json""
");
    }

    private static bool HasArg(string[] args, string name)
        => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static string? GetOpt(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return (i + 1 < args.Length) ? args[i + 1] : null;
        }
        return null;
    }

    private static int GetOptInt(string[] args, string name, int fallback)
    {
        var s = GetOpt(args, name);
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static string[] GetMultiOpt(string[] args, string name)
        => args.Select((v, i) => (v, i))
               .Where(t => string.Equals(t.v, name, StringComparison.OrdinalIgnoreCase) && t.i + 1 < args.Length)
               .Select(t => args[t.i + 1])
               .ToArray();

    private static int CmdList(string[] args)
    {
        var input = GetOpt(args, "--in") ?? GetOpt(args, "-i");
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("Missing --in");
            return 2;
        }

        var mat = MaterialIO.Load(input);

        var json = JsonSerializer.Serialize(mat, JsonOpts.Indented);
        Console.WriteLine(json);

        if (mat is BGSM)
        {
            Console.WriteLine();
            Console.WriteLine("ALIASES (BGSM)");
            Console.WriteLine("  Roughness -> Smoothness (stored as 1 - Roughness)");
            Console.WriteLine("  Specular  -> SpecularMult");
        }

        return 0;
    }

    private static int CmdSet(string[] args)
    {
        var input  = GetOpt(args, "--in")  ?? GetOpt(args, "-i");
        var output = GetOpt(args, "--out") ?? GetOpt(args, "-o");
        var parms  = GetMultiOpt(args, "--param");

        if (string.IsNullOrWhiteSpace(input))  { Console.Error.WriteLine("Missing --in");    return 2; }
        if (string.IsNullOrWhiteSpace(output)) { Console.Error.WriteLine("Missing --out");   return 2; }
        if (parms.Length == 0)                 { Console.Error.WriteLine("Missing --param"); return 2; }

        var mat = MaterialIO.Load(input);

        foreach (var p in parms)
        {
            var eq = p.IndexOf('=');
            if (eq <= 0 || eq == p.Length - 1)
            {
                Console.Error.WriteLine($"Bad --param '{p}' (expected Name=Value)");
                return 2;
            }

            var key = p[..eq].Trim();
            var val = p[(eq + 1)..].Trim();

            if (mat is BGSM bgsm)
            {
                if (key.Equals("Roughness", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseFloat(val, out var rough))
                    {
                        Console.Error.WriteLine($"Failed to parse Roughness '{val}' as float.");
                        return 2;
                    }
                    bgsm.Smoothness = Clamp01(1f - rough);
                    continue;
                }

                if (key.Equals("Specular", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseFloat(val, out var spec))
                    {
                        Console.Error.WriteLine($"Failed to parse Specular '{val}' as float.");
                        return 2;
                    }
                    bgsm.SpecularMult = spec;
                    continue;
                }
            }

            if (!TrySetProperty(mat, key, val, out var err))
            {
                Console.Error.WriteLine(err);
                return 2;
            }
        }

        MaterialIO.Save(mat, output);
        return 0;
    }

    private static int CmdSchema(string[] args)
    {
        var outPath = GetOpt(args, "--out") ?? GetOpt(args, "-o");
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Error.WriteLine("Missing --out");
            return 2;
        }

        var schema = SchemaBuilder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
        File.WriteAllText(outPath, JsonSerializer.Serialize(schema, JsonOpts.Indented));
        Console.WriteLine($"Schema written to {outPath}");
        return 0;
    }

    private static int CmdScan(string[] args)
    {
        var root = GetOpt(args, "--root") ?? GetOpt(args, "-r");
        var outPath = GetOpt(args, "--out") ?? GetOpt(args, "-o");

        if (string.IsNullOrWhiteSpace(root)) { Console.Error.WriteLine("Missing --root"); return 2; }
        if (string.IsNullOrWhiteSpace(outPath)) { Console.Error.WriteLine("Missing --out"); return 2; }
        if (!Directory.Exists(root)) { Console.Error.WriteLine($"Root dir not found: {root}"); return 2; }

        var threads = Math.Max(1, GetOptInt(args, "--threads", Environment.ProcessorCount));
        var includeDefaults = HasArg(args, "--include-defaults");
        var includeTextures = HasArg(args, "--textures");
        var limit = Math.Max(0, GetOptInt(args, "--limit", 0)); // 0 = no limit

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".bgsm", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".bgem", StringComparison.OrdinalIgnoreCase))
            .Take(limit > 0 ? limit : int.MaxValue)
            .ToArray();

        Console.WriteLine($"Scanning {files.Length} materials under: {root}");
        Console.WriteLine($"Threads: {threads}");

        var bgsmDefaults = new BGSM();
        var bgemDefaults = new BGEM();

        var results = new ConcurrentBag<ScanEntry>();
        var failures = new ConcurrentBag<ScanFailure>();

        Parallel.ForEach(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            file =>
            {
                try
                {
                    var mat = MaterialIO.Load(file);
                    var entry = ScanOne(file, mat, bgsmDefaults, bgemDefaults, includeDefaults, includeTextures);
                    results.Add(entry);
                }
                catch (Exception ex)
                {
                    failures.Add(new ScanFailure { Path = file, Error = ex.Message });
                }
            });

        var output = new ScanOutput
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Root = Path.GetFullPath(root),
            FileCount = files.Length,
            SuccessCount = results.Count,
            FailureCount = failures.Count,
            Failures = failures.OrderBy(f => f.Path).ToList(),
            Entries = results.OrderBy(e => e.Path).ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
        File.WriteAllText(outPath, JsonSerializer.Serialize(output, JsonOpts.Indented));
        Console.WriteLine($"Scan written to {outPath}");
        if (failures.Count > 0)
            Console.WriteLine($"Failures: {failures.Count} (see JSON)");

        return failures.Count > 0 ? 1 : 0;
    }

    private static ScanEntry ScanOne(
        string path,
        BaseMaterialFile mat,
        BGSM bgsmDefaults,
        BGEM bgemDefaults,
        bool includeDefaults,
        bool includeTextures)
    {
        var typeName = mat.GetType().Name;
        var props = SchemaBuilder.GetWritableProps(mat.GetType());

        object defaults = mat switch
        {
            BGSM => bgsmDefaults,
            BGEM => bgemDefaults,
            _ => Activator.CreateInstance(mat.GetType()) ?? new object()
        };

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in props)
        {
            if (!includeTextures && p.PropertyType == typeof(string) && p.Name.EndsWith("Texture", StringComparison.OrdinalIgnoreCase))
                continue;

            var v = p.GetValue(mat);
            if (includeDefaults)
            {
                dict[p.Name] = v;
                continue;
            }

            var dv = p.GetValue(defaults);
            if (!ValuesEqual(v, dv))
                dict[p.Name] = v;
        }

        dict["Version"] = mat.Version;

        return new ScanEntry
        {
            Path = path,
            Kind = typeName,
            Fields = dict
        };
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        if (a is float af && b is float bf) return af.Equals(bf);
        if (a is double ad && b is double bd) return ad.Equals(bd);

        return a.Equals(b);
    }

    private static int CmdSweep(string[] args)
    {
        var cfgPath = GetOpt(args, "--config") ?? GetOpt(args, "-c");
        if (string.IsNullOrWhiteSpace(cfgPath))
        {
            Console.Error.WriteLine("Missing --config");
            return 2;
        }

        Console.Error.WriteLine("Sweep not wired yet (config parsing stub).");
        return 2;
    }

    private static bool TrySetProperty(object target, string propertyName, string rawValue, out string? error)
    {
        error = null;

        var type = target.GetType();
        var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop == null || !prop.CanWrite)
        {
            error = $"Unknown or read-only property '{propertyName}' on {type.Name}.";
            return false;
        }

        try
        {
            var destType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object converted = ConvertTo(destType, rawValue);
            prop.SetValue(target, converted);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to set '{propertyName}': {ex.Message}";
            return false;
        }
    }

    private static bool TryParseFloat(string s, out float v)
        => float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);

    private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static object ConvertTo(Type destType, string raw)
    {
        if (destType == typeof(string)) return raw;
        if (destType == typeof(bool)) return bool.Parse(raw);
        if (destType == typeof(byte)) return byte.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (destType == typeof(int)) return int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (destType == typeof(uint)) return uint.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (destType == typeof(float)) return float.Parse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        if (destType == typeof(double)) return double.Parse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        if (destType.IsEnum) return Enum.Parse(destType, raw, ignoreCase: true);
        return Convert.ChangeType(raw, destType, CultureInfo.InvariantCulture);
    }

    private static class MaterialIO
    {
        public static BaseMaterialFile Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Input file not found.", path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".bgsm" => LoadTyped<BGSM>(path),
                ".bgem" => LoadTyped<BGEM>(path),
                _ => throw new NotSupportedException($"Unsupported extension '{ext}'. Expected .bgsm or .bgem.")
            };
        }

        private static T LoadTyped<T>(string path) where T : BaseMaterialFile, new()
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var m = new T();
            if (!m.Open(fs))
                throw new InvalidDataException($"Failed to parse '{path}' as {typeof(T).Name}.");
            return m;
        }

        public static void Save(BaseMaterialFile mat, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            if (!mat.Save(fs))
                throw new InvalidDataException($"Failed to write '{path}'.");
        }
    }

    private static class JsonOpts
    {
        public static readonly JsonSerializerOptions Indented = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
    }

    // ===== Schema support =====

    private sealed class MaterialSchema
    {
        public List<FieldDesc> BGSM { get; set; } = new();
        public List<FieldDesc> BGEM { get; set; } = new();
    }

    private sealed class FieldDesc
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsEnum { get; set; }
        public bool IsNullable { get; set; }
        public bool CanWrite { get; set; }
        public object? DefaultValue { get; set; }
        public string[]? EnumNames { get; set; }
    }

    private static class SchemaBuilder
    {
        public static MaterialSchema Build()
        {
            return new MaterialSchema
            {
                BGSM = Describe(typeof(BGSM)),
                BGEM = Describe(typeof(BGEM))
            };
        }

        public static List<PropertyInfo> GetWritableProps(Type t)
            => t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.GetMethod != null)
                .Where(p => p.Name != "Version")
                .ToList();

        private static List<FieldDesc> Describe(Type t)
        {
            object instance = Activator.CreateInstance(t) ?? throw new InvalidOperationException($"Failed to create {t.Name}");
            var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var list = new List<FieldDesc>(props.Length);

            foreach (var p in props)
            {
                var pt = p.PropertyType;
                var nullableUnderlying = Nullable.GetUnderlyingType(pt);
                var isNullable = nullableUnderlying != null;
                var destType = nullableUnderlying ?? pt;

                object? def = null;
                try { def = p.GetValue(instance); } catch { }

                var desc = new FieldDesc
                {
                    Name = p.Name,
                    Type = destType.FullName ?? destType.Name,
                    IsEnum = destType.IsEnum,
                    IsNullable = isNullable,
                    CanWrite = p.CanWrite,
                    DefaultValue = def
                };

                if (destType.IsEnum)
                    desc.EnumNames = Enum.GetNames(destType);

                list.Add(desc);
            }

            return list;
        }
    }

    // ===== Scan output types =====

    private sealed class ScanOutput
    {
        public DateTime GeneratedAtUtc { get; set; }
        public string Root { get; set; } = "";
        public int FileCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<ScanFailure> Failures { get; set; } = new();
        public List<ScanEntry> Entries { get; set; } = new();
    }

    private sealed class ScanFailure
    {
        public string Path { get; set; } = "";
        public string Error { get; set; } = "";
    }

    private sealed class ScanEntry
    {
        public string Path { get; set; } = "";
        public string Kind { get; set; } = "";
        public Dictionary<string, object?> Fields { get; set; } = new();
    }
}

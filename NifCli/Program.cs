// Program_parallel.cs - NifCli with batch + parallel convert-sss
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NifCli;

internal static class Program
{
    // Commands:
    //   NifCli help
    //   NifCli list        --in <file.nif> [--show-enums]
    //   NifCli set-flag    --in <file.nif> --out <file.nif> --flag <NAME> [--enable true|false] [--flags2] [--filter <TypeSubstr>]
    //   NifCli convert-sss --in <file.nif> --out <file.nif> [--filter <TypeSubstr>] [--mode aggressive|safe] [--all]
    //   NifCli convert-sss --root <folder> --outdir <folder> [--mode aggressive|safe] [--all] [--filter <TypeSubstr>] [--parallel N] [--progress N] [--overwrite]
    //   NifCli scan        --root <folder> [--filter <TypeSubstr>] [--flags2] [--limit N]
    //   NifCli dump        --root <folder> --out <file.jsonl> [--include-textures] [--filter <TypeSubstr>] [--progress N]

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        var opt = ParseArgs(args.Skip(1).ToArray());

        try
        {
            return cmd switch
            {
                "list" => CmdList(opt),
                "set-flag" => CmdSetFlag(opt),
                "convert-sss" => CmdConvertSss(opt),
                "scan" => CmdScan(opt),
                "dump" => CmdDump(opt),
                _ => Fail($"Unknown command '{cmd}'. Use: NifCli help"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
NifCli - FO4 NIF batch inspector/patcher (shader flags pipeline)

Commands:
  list        --in <file.nif> [--show-enums]
  set-flag    --in <file.nif> --out <file.nif> --flag <NAME> [--enable true|false] [--flags2] [--filter <TypeSubstr>]

  convert-sss (single file)
      --in <file.nif> --out <file.nif> [--filter <TypeSubstr>] [--mode aggressive|safe] [--all]

  convert-sss (batch folder, parallel)
      --root <folder> --outdir <folder> [--mode aggressive|safe] [--all] [--filter <TypeSubstr>]
      [--parallel N] [--progress N] [--overwrite]

  scan        --root <folder> [--filter <TypeSubstr>] [--flags2] [--limit N]
  dump        --root <folder> --out <file.jsonl> [--include-textures] [--filter <TypeSubstr>] [--progress N]

Notes:
- Uses NiflySharp via reflection (no compile-time dependency on its namespace).
- convert-sss:
    * default: converts only ShaderType=Default blocks to SkinTint (SSS)
    * --all:    NUCLEAR MODE - converts ALL shader-ish blocks that have ShaderType+SkinTint (not just Default)
    * aggressive: forces SPF2 to exactly 129 (bits 0+7)
    * safe: ORs bits 0+7 into existing SPF2
");
    }

    // ----------------------------
    // NiflySharp reflection bridge
    // ----------------------------
    private static class Nif
    {
        private static readonly object _lock = new();
        private static Assembly? _asm;
        private static Type? _nifFileType;

        public static void EnsureLoaded()
        {
            if (_asm != null && _nifFileType != null) return;

            lock (_lock)
            {
                if (_asm != null && _nifFileType != null) return;

                // Prefer loading from the app base so "bin\Release\..." drop-ins work.
                _asm = TryLoadFromAppBase("NiflySharp.dll")
                       ?? TryLoadFromAppBase("niflysharp.dll")
                       ?? TryLoad("NiflySharp")
                       ?? TryLoad("niflysharp")
                       ?? TryLoadFromNugetCacheFallback();

                if (_asm == null)
                    throw new InvalidOperationException(
                        "Failed to load NiflySharp assembly. Ensure NiflySharp.dll (and native deps if any) are beside NifCli in the output folder.");

                _nifFileType = _asm.GetTypes().FirstOrDefault(t => t.Name.Equals("NifFile", StringComparison.OrdinalIgnoreCase));
                if (_nifFileType == null)
                    throw new InvalidOperationException("Could not find type 'NifFile' inside the loaded NiflySharp assembly.");
            }
        }

        private static Assembly? TryLoad(string simpleName)
        {
            try { return Assembly.Load(simpleName); }
            catch { return null; }
        }

        private static Assembly? TryLoadFromAppBase(string fileName)
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                if (string.IsNullOrWhiteSpace(baseDir)) return null;

                var dllPath = Path.Combine(baseDir, fileName);
                if (!File.Exists(dllPath)) return null;

                return Assembly.LoadFrom(dllPath);
            }
            catch
            {
                return null;
            }
        }

        private static Assembly? TryLoadFromNugetCacheFallback()
        {
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home)) return null;

                var packagesRoot = Path.Combine(home, ".nuget", "packages");
                if (!Directory.Exists(packagesRoot)) return null;

                var candidates = new List<string>();

                var niflyRoot = Path.Combine(packagesRoot, "nifly");
                if (Directory.Exists(niflyRoot))
                    candidates.AddRange(Directory.EnumerateFiles(niflyRoot, "NiflySharp.dll", SearchOption.AllDirectories));

                var niflySharpRoot = Path.Combine(packagesRoot, "niflysharp");
                if (Directory.Exists(niflySharpRoot))
                    candidates.AddRange(Directory.EnumerateFiles(niflySharpRoot, "niflysharp.dll", SearchOption.AllDirectories));

                var dll = candidates
                    .Where(File.Exists)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.Length)
                    .Select(fi => fi.FullName)
                    .FirstOrDefault();

                return dll == null ? null : Assembly.LoadFrom(dll);
            }
            catch
            {
                return null;
            }
        }

        public static object NewNifFile()
        {
            EnsureLoaded();
            return Activator.CreateInstance(_nifFileType!)!;
        }

        public static int Load(object nifFile, string path)
        {
            var m = FindMethodByNameAndFirstParam(nifFile.GetType(), "Load", typeof(string));
            if (m == null) throw new MissingMethodException("NifFile.Load(...) not found (no overload with first parameter string).");

            var args = BuildArgs(m, path);
            var rc = m.Invoke(nifFile, args);
            return rc == null ? 0 : Convert.ToInt32(rc, CultureInfo.InvariantCulture);
        }

        public static void Save(object nifFile, string path)
        {
            var m = FindMethodByNameAndFirstParam(nifFile.GetType(), "Save", typeof(string));
            if (m == null) throw new MissingMethodException("NifFile.Save(...) not found (no overload with first parameter string).");

            var args = BuildArgs(m, path);
            m.Invoke(nifFile, args);
        }

        private static MethodInfo? FindMethodByNameAndFirstParam(Type t, string name, Type firstParam)
        {
            return t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(mi => mi.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Where(mi =>
                {
                    var ps = mi.GetParameters();
                    return ps.Length >= 1 && ps[0].ParameterType == firstParam;
                })
                .OrderBy(mi => mi.GetParameters().Length)
                .FirstOrDefault();
        }

        private static object?[] BuildArgs(MethodInfo mi, string first)
        {
            var ps = mi.GetParameters();
            var args = new object?[ps.Length];
            args[0] = first;

            for (int i = 1; i < ps.Length; i++)
            {
                var p = ps[i];
                if (p.HasDefaultValue) args[i] = p.DefaultValue;
                else if (p.ParameterType.IsValueType) args[i] = Activator.CreateInstance(p.ParameterType);
                else args[i] = null;
            }

            return args;
        }

        public static bool GetBool(object obj, string prop)
        {
            var p = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return false;
            return p.GetValue(obj) is bool b && b;
        }

        public static object? GetProp(object obj, string prop)
        {
            var p = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(obj);
        }

        public static void SetProp(object obj, string prop, object value)
        {
            var p = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return;
            p.SetValue(obj, value);
        }

        public static IList GetBlocks(object nifFile)
        {
            var p = nifFile.GetType().GetProperty("Blocks", BindingFlags.Public | BindingFlags.Instance);
            if (p == null) throw new MissingMemberException("NifFile.Blocks not found.");
            var v = p.GetValue(nifFile);
            if (v is IList list) return list;
            throw new InvalidOperationException("NifFile.Blocks is not IList.");
        }

        public static bool GetValid(object nifFile) => GetBool(nifFile, "Valid");
        public static bool GetHasUnknownBlocks(object nifFile) => GetBool(nifFile, "HasUnknownBlocks");
    }

    // ----------------------------
    // list
    // ----------------------------
    private static int CmdList(Dictionary<string, string?> opt)
    {
        var input = Require(opt, "in");
        var showEnums = HasBool(opt, "show-enums");

        if (!File.Exists(input))
            throw new FileNotFoundException("Input file not found.", input);

        if (showEnums)
        {
            Nif.EnsureLoaded();
            var nifAsm = GetLoadedNifAsm();
            Console.WriteLine("=== Enums in NiflySharp ===");
            foreach (var t in nifAsm.GetTypes().Where(t => t.IsEnum).OrderBy(t => t.FullName).Take(400))
                Console.WriteLine($"-- {t.FullName}");
            Console.WriteLine();
        }

        var nif = Nif.NewNifFile();
        var rc = Nif.Load(nif, input);
        var valid = Nif.GetValid(nif);
        var unknown = Nif.GetHasUnknownBlocks(nif);
        var blocks = Nif.GetBlocks(nif);

        Console.WriteLine($"NIF: {input}");
        Console.WriteLine($"LoadRC: {rc}  Valid: {valid}  UnknownBlocks: {unknown}");
        Console.WriteLine($"Blocks: {blocks.Count}");

        int idx = 0;
        foreach (var b in blocks)
        {
            idx++;
            var t = b.GetType().Name;

            if (IsShaderBlockType(t))
            {
                Console.WriteLine();
                Console.WriteLine($"[{idx}] {t}");

                DumpIfExists(b, "Name");
                DumpIfExists(b, "Type");
                DumpIfExists(b, "ShaderType");
                DumpIfExists(b, "ShaderType_SK_FO4");

                DumpIfExists(b, "ShaderFlags_F4SPF1");
                DumpIfExists(b, "ShaderFlags_F4SPF2");
                DumpIfExists(b, "ShaderFlagsList1");
                DumpIfExists(b, "ShaderFlagsList2");

                DumpIfExists(b, "ShaderFlags");
                DumpIfExists(b, "ShaderFlags2");

                DumpIfExists(b, "DoubleSided");
                DumpIfExists(b, "HasVertexAlpha");
                DumpIfExists(b, "HasGreyscaleToPaletteAlpha");
                DumpIfExists(b, "HasTextureSet");
                DumpIfExists(b, "TextureSetRef");
            }
        }

        return 0;
    }

    // ----------------------------
    // set-flag
    // ----------------------------
    private static int CmdSetFlag(Dictionary<string, string?> opt)
    {
        var input = Require(opt, "in");
        var output = Require(opt, "out");
        var flag = Require(opt, "flag");

        var enable = GetBool(opt, "enable", true);
        var flags2 = HasBool(opt, "flags2");

        var filter = opt.TryGetValue("filter", out var f) && !string.IsNullOrWhiteSpace(f)
            ? f!
            : "BSLightingShaderProperty";

        if (!File.Exists(input))
            throw new FileNotFoundException("Input file not found.", input);

        var nif = Nif.NewNifFile();
        var rc = Nif.Load(nif, input);
        if (rc != 0 || !Nif.GetValid(nif))
            return Fail($"Load failed rc={rc} valid={Nif.GetValid(nif)}");

        var blocks = Nif.GetBlocks(nif);

        string[] candidates = flags2
            ? new[] { "ShaderFlags_F4SPF2", "ShaderFlags2" }
            : new[] { "ShaderFlags_F4SPF1", "ShaderFlags" };

        int touched = 0;

        foreach (var b in blocks)
        {
            var typeName = b.GetType().Name;
            if (!typeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            var propName = candidates.FirstOrDefault(p => b.GetType().GetProperty(p, BindingFlags.Public | BindingFlags.Instance)?.PropertyType.IsEnum == true);
            if (propName == null) continue;

            if (!TryGetEnumValue(b, propName, out var enumType, out var currentValue)) continue;
            if (!TryParseEnumByName(enumType, flag, out var flagValue)) continue;

            var newValue = enable ? OrEnum(currentValue, flagValue) : AndNotEnum(currentValue, flagValue);

            if (!Equals(newValue, currentValue))
            {
                Nif.SetProp(b, propName, newValue);
                touched++;
            }
        }

        Nif.Save(nif, output);
        Console.WriteLine($"Patched blocks: {touched}");
        Console.WriteLine($"Wrote: {output}");
        return 0;
    }

    // ----------------------------
    // convert-sss
    // ----------------------------
    // - single: uses --in/--out
    // - batch:  uses --root/--outdir, parallel by default
    private static int CmdConvertSss(Dictionary<string, string?> opt)
    {
        if (opt.ContainsKey("root"))
            return CmdConvertSssBatch(opt);

        return CmdConvertSssSingle(opt);
    }

    private static int CmdConvertSssSingle(Dictionary<string, string?> opt)
    {
        var input = Require(opt, "in");
        var output = Require(opt, "out");

        var mode = opt.TryGetValue("mode", out var m) && !string.IsNullOrWhiteSpace(m) ? m!.Trim() : "aggressive";
        var aggressive = mode.Equals("aggressive", StringComparison.OrdinalIgnoreCase);

        var all = HasBool(opt, "all");

        var filter = opt.TryGetValue("filter", out var f) && !string.IsNullOrWhiteSpace(f)
            ? f!
            : (all ? "" : "BSLightingShaderProperty");

        if (!File.Exists(input))
            throw new FileNotFoundException("Input file not found.", input);

        var res = ConvertOne(input, output, filter, aggressive, all, overwrite: true, out var msg);
        Console.WriteLine(msg);
        return res ? 0 : 1;
    }

    private static int CmdConvertSssBatch(Dictionary<string, string?> opt)
    {
        var root = Require(opt, "root");
        var outDir = Require(opt, "outdir");

        var mode = opt.TryGetValue("mode", out var m) && !string.IsNullOrWhiteSpace(m) ? m!.Trim() : "aggressive";
        var aggressive = mode.Equals("aggressive", StringComparison.OrdinalIgnoreCase);

        var all = HasBool(opt, "all");

        var filter = opt.TryGetValue("filter", out var f) && !string.IsNullOrWhiteSpace(f)
            ? f!
            : (all ? "" : "BSLightingShaderProperty");

        var overwrite = HasBool(opt, "overwrite");
        var parallel = GetInt(opt, "parallel", Environment.ProcessorCount);
        if (parallel <= 0) parallel = Environment.ProcessorCount;

        var progressEvery = GetInt(opt, "progress", 500);

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Root not found: {root}");

        Directory.CreateDirectory(outDir);

        var files = Directory.EnumerateFiles(root, "*.nif", SearchOption.AllDirectories).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("No .nif files found.");
            return 0;
        }

        // Make sure reflection loads once (helps avoid racing Assembly.LoadFrom)
        Nif.EnsureLoaded();

        long scanned = 0, written = 0, skipped = 0, failed = 0;
        long changedType = 0, changedF1 = 0, changedF2 = 0, skippedNoSkinTint = 0, scannedShaderTypeBlocks = 0;

        // Optional: allow redirecting error logs away from game folders.
        var errorLogPath = opt.TryGetValue("errorlog", out var el) && !string.IsNullOrWhiteSpace(el)
            ? el!
            : Path.Combine(outDir, "convert-sss.errors.tsv");

        var errorSummaryPath = opt.TryGetValue("errorsummary", out var es) && !string.IsNullOrWhiteSpace(es)
            ? es!
            : Path.Combine(outDir, "convert-sss.error_summary.tsv");

        static string OneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return s.Length <= max ? s : s[..max];
        }

        var errorCounts = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        
        // Incremental error logging (so you can inspect failures while the batch is still running).
        // We write the header immediately and append a line per failure. This survives Ctrl+C / crashes.
        Directory.CreateDirectory(Path.GetDirectoryName(errorLogPath)!);

        StreamWriter? errorWriter = null;
        object errorWriterLock = new object();
        try
        {
        errorWriter = new StreamWriter(errorLogPath, append: false);
        errorWriter.WriteLine("file\terrorKind\tmessage\twhere");
        errorWriter.Flush();
        }
        catch
        {
        // If we can't create the error log, we still continue the batch; failures will be counted only.
        errorWriter = null;
        }

        var consoleLock = new object();
        var po = new ParallelOptions { MaxDegreeOfParallelism = parallel };

        Parallel.ForEach(files, po, inPath =>
        {
            try
            {
                var rel = Path.GetRelativePath(root, inPath);
                var outPath = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                var ok = ConvertOne(
                    inPath,
                    outPath,
                    filter,
                    aggressive,
                    all,
                    overwrite,
                    out var msg,
                    out var cType,
                    out var cF1,
                    out var cF2,
                    out var sNoSkin,
                    out var sBlocks);

                Interlocked.Increment(ref scanned);

                if (!ok)
                {
                    Interlocked.Increment(ref skipped);
                }
                else
                {
                    Interlocked.Increment(ref written);
                }

                Interlocked.Add(ref changedType, cType);
                Interlocked.Add(ref changedF1, cF1);
                Interlocked.Add(ref changedF2, cF2);
                Interlocked.Add(ref skippedNoSkinTint, sNoSkin);
                Interlocked.Add(ref scannedShaderTypeBlocks, sBlocks);

                var s = Interlocked.Read(ref scanned);
                if (progressEvery > 0 && (s % progressEvery) == 0)
                {
                    lock (consoleLock)
                    {
                        Console.WriteLine($"convert-sss batch: scanned={s} written={Interlocked.Read(ref written)} skipped={Interlocked.Read(ref skipped)} failed={Interlocked.Read(ref failed)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref scanned);
                Interlocked.Increment(ref failed);

                var kind = ex.GetType().Name;
                errorCounts.AddOrUpdate(kind, 1, (_, v) => v + 1);

                var msg = OneLine(ex.Message, 320);
                var where = OneLine(ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "", 240);

                
// TSV: file<TAB>errorKind<TAB>message<TAB>where
if (errorWriter != null)
{
    lock (errorWriterLock)
    {
        try
        {
            errorWriter.WriteLine($"{inPath}	{kind}	{msg}	{where}");
            errorWriter.Flush();
        }
        catch { /* ignore */ }
    }
}
}
        });

        
// Close the incremental writer (best-effort).
try { errorWriter?.Dispose(); } catch { /* ignore */ }

// Write error summary at the end (if anything failed).
if (!errorCounts.IsEmpty)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(errorSummaryPath)!);
        var summary = errorCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}\t{kv.Value}");
        File.WriteAllLines(errorSummaryPath, new[] { "errorKind\tcount" }.Concat(summary));
        Console.WriteLine($"Error summary: {errorSummaryPath}");

        // Print top error kinds to console for quick triage.
        var top = errorCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(8)
            .Select(kv => $"{kv.Key}={kv.Value}");
        Console.WriteLine("Top errors: " + string.Join(", ", top));
    }
    catch { /* ignore */ }

    // The per-file TSV was written incrementally (if we could open it).
    Console.WriteLine($"Errors written (incremental): {errorLogPath}");
}

Console.WriteLine($"convert-sss batch DONE: mode={mode} all={all} filter='{filter}' parallel={parallel} overwrite={overwrite}");
        Console.WriteLine($"files: scanned={scanned} written={written} skipped={skipped} failed={failed}");
        Console.WriteLine($"shader blocks: ShaderType->SkinTint={changedType} SPF1-mod={changedF1} SPF2-mod={changedF2} scannedShaderTypeBlocks={scannedShaderTypeBlocks} skipped(no SkinTint)={skippedNoSkinTint}");
        Console.WriteLine($"outdir: {outDir}");
        return failed > 0 ? 2 : 0;
    }

    private static bool ConvertOne(
        string input,
        string output,
        string filter,
        bool aggressive,
        bool all,
        bool overwrite,
        out string message)
    {
        return ConvertOne(input, output, filter, aggressive, all, overwrite, out message,
            out _, out _, out _, out _, out _);
    }

    private static bool ConvertOne(
        string input,
        string output,
        string filter,
        bool aggressive,
        bool all,
        bool overwrite,
        out string message,
        out int changedType,
        out int changedF1,
        out int changedF2,
        out int skippedNoSkinTint,
        out int scannedShaderTypeBlocks)
    {
        changedType = 0; changedF1 = 0; changedF2 = 0; skippedNoSkinTint = 0; scannedShaderTypeBlocks = 0;

        if (!File.Exists(input))
        {
            message = $"Missing: {input}";
            return false;
        }

        if (!overwrite && File.Exists(output))
        {
            message = $"Skip (exists): {output}";
            return false;
        }

        var nif = Nif.NewNifFile();
        var rc = Nif.Load(nif, input);
        if (rc != 0 || !Nif.GetValid(nif))
        {
            message = $"Skip (load fail): rc={rc} valid={Nif.GetValid(nif)} in={input}";
            return false;
        }

        var blocks = Nif.GetBlocks(nif);

        foreach (var b in blocks)
        {
            var typeName = b.GetType().Name;

            if (!string.IsNullOrEmpty(filter))
            {
                if (!typeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            else
            {
                if (!IsShaderBlockType(typeName))
                    continue;
            }

            var shaderTypeProp = b.GetType().GetProperty("ShaderType", BindingFlags.Public | BindingFlags.Instance);
            if (shaderTypeProp == null || !shaderTypeProp.PropertyType.IsEnum)
                continue;

            scannedShaderTypeBlocks++;

            var shaderTypeEnum = shaderTypeProp.PropertyType;
            var curShaderType = shaderTypeProp.GetValue(b);
            if (curShaderType == null) continue;

            if (!all && !curShaderType.ToString()!.Equals("Default", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryParseEnumByName(shaderTypeEnum, "SkinTint", out var skinType))
            {
                skippedNoSkinTint++;
                continue;
            }

            if (!Equals(curShaderType, skinType))
            {
                shaderTypeProp.SetValue(b, skinType);
                changedType++;
            }

            if (TryGetEnumValue(b, "ShaderFlags_F4SPF1", out _, out var f1Cur))
            {
                var f1New = OrEnumWithBit(f1Cur, 21);
                if (!Equals(f1New, f1Cur))
                {
                    Nif.SetProp(b, "ShaderFlags_F4SPF1", f1New);
                    changedF1++;
                }
            }

            if (TryGetEnumValue(b, "ShaderFlags_F4SPF2", out var f2Type, out var f2Cur))
            {
                var target = Enum.ToObject(f2Type, (ulong)129);

                object f2New = aggressive ? target : OrEnum(f2Cur, target);

                if (!Equals(f2New, f2Cur))
                {
                    Nif.SetProp(b, "ShaderFlags_F4SPF2", f2New);
                    changedF2++;
                }
            }
        }

        // If nothing changed, treat as a skip (important for large corpus runs).
        if (changedType == 0 && changedF1 == 0 && changedF2 == 0)
        {
            message = $"Skip (no changes): in={input} scannedBlocks={scannedShaderTypeBlocks}";
            return false;
        }

        // Atomic-ish write: save to temp then replace
        var tmp = output + ".tmp";
        if (File.Exists(tmp)) TryDelete(tmp);

        Nif.Save(nif, tmp);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
        {
            try { File.SetAttributes(output, FileAttributes.Normal); } catch { }
        }

        // .NET 8 supports overwrite=true
        File.Move(tmp, output, overwrite: true);
        message = $"convert-sss: in={input} out={output} type={changedType} f1={changedF1} f2={changedF2} scannedBlocks={scannedShaderTypeBlocks} skippedNoSkin={skippedNoSkinTint}";
        return true;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    // ----------------------------
    // scan
    // ----------------------------
    private static int CmdScan(Dictionary<string, string?> opt)
    {
        var root = Require(opt, "root");
        var flags2 = HasBool(opt, "flags2");

        var filter = opt.TryGetValue("filter", out var f) && !string.IsNullOrWhiteSpace(f)
            ? f!
            : "BSLightingShaderProperty";

        var limit = GetInt(opt, "limit", 0);

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Root not found: {root}");

        string[] candidates = flags2
            ? new[] { "ShaderFlags_F4SPF2", "ShaderFlags2" }
            : new[] { "ShaderFlags_F4SPF1", "ShaderFlags" };

        IEnumerable<string> files = Directory.EnumerateFiles(root, "*.nif", SearchOption.AllDirectories);
        if (limit > 0) files = files.Take(limit);

        var setNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int scanned = 0;
        foreach (var file in files)
        {
            scanned++;
            try
            {
                var nif = Nif.NewNifFile();
                var rc = Nif.Load(nif, file);
                if (rc != 0 || !Nif.GetValid(nif)) continue;

                var blocks = Nif.GetBlocks(nif);

                foreach (var b in blocks)
                {
                    var typeName = b.GetType().Name;
                    if (!typeName.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

                    var propName = candidates.FirstOrDefault(p => b.GetType().GetProperty(p, BindingFlags.Public | BindingFlags.Instance)?.PropertyType.IsEnum == true);
                    if (propName == null) continue;

                    if (!TryGetEnumValue(b, propName, out var enumType, out var currentValue)) continue;

                    foreach (var name in Enum.GetNames(enumType))
                    {
                        if (!TryParseEnumByName(enumType, name, out var val)) continue;
                        if (HasFlag(currentValue, val))
                            setNames[name] = setNames.TryGetValue(name, out var c) ? c + 1 : 1;
                    }
                }
            }
            catch { }
        }

        Console.WriteLine($"Scanned NIFs: {scanned}");
        Console.WriteLine($"=== Top set flags (flags2={flags2}) for blocks matching '{filter}' ===");
        foreach (var kv in setNames.OrderByDescending(k => k.Value).Take(120))
            Console.WriteLine($"{kv.Key,-36} {kv.Value}");


        return 0;
    }

    // ----------------------------
    // dump (JSONL)
    // ----------------------------
    private static int CmdDump(Dictionary<string, string?> opt)
    {
        var root = Require(opt, "root");
        var outPath = Require(opt, "out");

        var includeTextures = HasBool(opt, "include-textures");
        var progressEvery = GetInt(opt, "progress", 1000);

        var filter = opt.TryGetValue("filter", out var f) && !string.IsNullOrWhiteSpace(f) ? f! : "";

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Root not found: {root}");

        var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrWhiteSpace(outDir))
            Directory.CreateDirectory(outDir);

        var files = Directory.EnumerateFiles(root, "*.nif", SearchOption.AllDirectories);

        int scanned = 0, written = 0, failed = 0;

        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var sw = new StreamWriter(fs);

        foreach (var file in files)
        {
            scanned++;

            try
            {
                var nif = Nif.NewNifFile();
                var rc = Nif.Load(nif, file);
                if (rc != 0 || !Nif.GetValid(nif)) { failed++; continue; }

                var blocks = Nif.GetBlocks(nif);

                for (int i = 0; i < blocks.Count; i++)
                {
                    var b = blocks[i];
                    var t = b.GetType().Name;

                    if (!IsShaderBlockType(t)) continue;
                    if (!string.IsNullOrEmpty(filter) && !t.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

                    var rec = new Dictionary<string, object?>()
                    {
                        ["nif"] = file,
                        ["blockIndex"] = i,
                        ["blockType"] = t
                    };

                    AddIfExists(rec, b, "Name");
                    AddIfExists(rec, b, "Type");
                    AddIfExists(rec, b, "ShaderType");
                    AddIfExists(rec, b, "ShaderType_SK_FO4");

                    AddIfExists(rec, b, "ShaderFlags_F4SPF1");
                    AddIfExists(rec, b, "ShaderFlags_F4SPF2");
                    AddIfExists(rec, b, "ShaderFlagsList1");
                    AddIfExists(rec, b, "ShaderFlagsList2");

                    AddIfExists(rec, b, "ShaderFlags");
                    AddIfExists(rec, b, "ShaderFlags2");

                    AddIfExists(rec, b, "DoubleSided");
                    AddIfExists(rec, b, "HasVertexAlpha");
                    AddIfExists(rec, b, "HasGreyscaleToPaletteAlpha");

                    if (includeTextures)
                        TryAddTextureSet(rec, blocks, b);

                    sw.WriteLine(JsonSerializer.Serialize(rec));
                    written++;
                }
            }
            catch
            {
                failed++;
            }

            if (progressEvery > 0 && scanned % progressEvery == 0)
                Console.WriteLine($"dump: scanned={scanned} written={written} failed={failed}");
        }

        Console.WriteLine($"dump DONE: scanned={scanned} written={written} failed={failed}");
        Console.WriteLine($"Wrote: {outPath}");
        return 0;
    }

    private static bool IsShaderBlockType(string typeName) =>
        typeName.Contains("BSLightingShaderProperty", StringComparison.OrdinalIgnoreCase) ||
        typeName.Contains("BSShaderPPLightingProperty", StringComparison.OrdinalIgnoreCase) ||
        typeName.Contains("BSEffectShaderProperty", StringComparison.OrdinalIgnoreCase);

    private static void TryAddTextureSet(Dictionary<string, object?> rec, IList blocks, object shaderBlock)
    {
        var hasTS = Nif.GetBool(shaderBlock, "HasTextureSet");
        if (!hasTS) return;

        var tsRef = Nif.GetProp(shaderBlock, "TextureSetRef");
        if (tsRef == null) return;

        var idxProp = tsRef.GetType().GetProperty("Index", BindingFlags.Public | BindingFlags.Instance);
        if (idxProp == null) return;

        var tsIndexObj = idxProp.GetValue(tsRef);
        if (tsIndexObj == null) return;

        var tsIndex = Convert.ToInt32(tsIndexObj, CultureInfo.InvariantCulture);
        rec["textureSetIndex"] = tsIndex;

        if (tsIndex < 0 || tsIndex >= blocks.Count) return;

        var tsBlock = blocks[tsIndex];
        AddIfExists(rec, tsBlock, "NumTextures");
        AddIfExists(rec, tsBlock, "Textures");
    }

    private static void AddIfExists(Dictionary<string, object?> rec, object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (p == null) return;
        rec[propName] = p.GetValue(obj);
    }

    private static void DumpIfExists(object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (p == null) return;
        var v = p.GetValue(obj);
        Console.WriteLine($"  {propName}: {v}");
    }

    private static Assembly GetLoadedNifAsm()
    {
        Nif.EnsureLoaded();
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a =>
                a.GetName().Name?.Equals("NiflySharp", StringComparison.OrdinalIgnoreCase) == true ||
                a.GetName().Name?.Equals("niflysharp", StringComparison.OrdinalIgnoreCase) == true)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetTypes().Any(t => t.Name.Equals("NifFile", StringComparison.OrdinalIgnoreCase)));

        return asm ?? throw new InvalidOperationException("Could not locate loaded NiflySharp assembly.");
    }

    // ----------------------------
    // Arg parsing
    // ----------------------------
    private static Dictionary<string, string?> ParseArgs(string[] args)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;

            var key = a[2..];
            string? val = null;

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                val = args[i + 1];
                i++;
            }

            d[key] = val;
        }
        return d;
    }

    private static string Require(Dictionary<string, string?> opt, string key) =>
        opt.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v!
            : throw new ArgumentException($"Missing --{key}");

    private static bool HasBool(Dictionary<string, string?> opt, string key) =>
        opt.ContainsKey(key) && (opt[key] == null || GetBool(opt, key, true));

    private static bool GetBool(Dictionary<string, string?> opt, string key, bool def)
    {
        if (!opt.TryGetValue(key, out var v) || v == null) return def;
        return bool.TryParse(v, out var b) ? b : def;
    }

    private static int GetInt(Dictionary<string, string?> opt, string key, int def)
    {
        if (!opt.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return def;
        return int.TryParse(v, out var i) ? i : def;
    }

    private static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 1;
    }

    // ----------------------------
    // Enum helpers
    // ----------------------------
    private static bool TryGetEnumValue(object obj, string propName, out Type enumType, out object current)
    {
        enumType = typeof(object);
        current = new object();

        var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (p == null) return false;

        var t = p.PropertyType;
        if (!t.IsEnum) return false;

        enumType = t;
        current = p.GetValue(obj)!;
        return true;
    }

    private static bool TryParseEnumByName(Type enumType, string name, out object value)
    {
        value = Activator.CreateInstance(enumType)!;
        try
        {
            value = Enum.Parse(enumType, name, ignoreCase: true);
            return true;
        }
        catch { return false; }
    }

    private static object OrEnum(object a, object b)
    {
        var t = a.GetType();
        var ua = Convert.ToUInt64(a, CultureInfo.InvariantCulture);
        var ub = Convert.ToUInt64(b, CultureInfo.InvariantCulture);
        return Enum.ToObject(t, ua | ub);
    }

    private static object AndNotEnum(object a, object b)
    {
        var t = a.GetType();
        var ua = Convert.ToUInt64(a, CultureInfo.InvariantCulture);
        var ub = Convert.ToUInt64(b, CultureInfo.InvariantCulture);
        return Enum.ToObject(t, ua & ~ub);
    }

    private static bool HasFlag(object a, object b)
    {
        var ua = Convert.ToUInt64(a, CultureInfo.InvariantCulture);
        var ub = Convert.ToUInt64(b, CultureInfo.InvariantCulture);
        return ub != 0 && (ua & ub) == ub;
    }

    private static object OrEnumWithBit(object enumValue, int bit)
    {
        var t = enumValue.GetType();
        var u = Convert.ToUInt64(enumValue, CultureInfo.InvariantCulture);
        u |= (1UL << bit);
        return Enum.ToObject(t, u);
    }
}
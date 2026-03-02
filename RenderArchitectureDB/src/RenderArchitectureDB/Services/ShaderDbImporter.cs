
using RenderArchitectureDB.Model;
using RenderArchitectureDB.Util;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RenderArchitectureDB.Services;

public static class ShaderDbImporter
{
    /// <summary>
    /// Reads FO4ShaderDB:
    ///   master_index.json (optional)
    ///   structural/*.json (required for clustering/fingerprint)
    ///   reflection/*.json (optional for richer metrics)
    ///
    /// Expected structural json minimal fields:
    ///   instruction_count, texture_count, cube_count, cbuffer_layout_hash, resource_signature, fingerprint
    ///
    /// If your census outputs differ, adjust this importer only (keep schema stable downstream).
    /// </summary>
    public static List<ShaderRecord> Load(string fo4ShaderDbDir)
    {
        if (!Directory.Exists(fo4ShaderDbDir))
            throw new DirectoryNotFoundException($"ShaderDB directory not found: {fo4ShaderDbDir}");

        var structuralDir = Path.Combine(fo4ShaderDbDir, "structural");
        if (!Directory.Exists(structuralDir))
            throw new DirectoryNotFoundException($"ShaderDB missing structural folder: {structuralDir}");

        var shaderRecords = new List<ShaderRecord>();

        foreach (var file in Directory.GetFiles(structuralDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;

            // infer stage + hash from filename convention: ps_<hash>.json / vs_<hash>.json
            var name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_', 2);
            if (parts.Length != 2) continue;

            var stagePrefix = parts[0].ToLowerInvariant(); // ps/vs
            var stage = stagePrefix == "ps" ? "PS" : stagePrefix == "vs" ? "VS" : stagePrefix.ToUpperInvariant();
            var hash = parts[1];

            // Be liberal in what we accept:
            // - snake_case (census output)
            // - camelCase / PascalCase (hand-authored / other tools)
            int instructionCount = GetIntAny(root, "instruction_count", "instructionCount", "InstructionCount");
            int textureCount = GetIntAny(root, "texture_count", "textureCount", "TextureCount");
            int cubeCount = GetIntAny(root, "cube_count", "cubeCount", "CubeCount");
            string cbufHash = GetStrAny(root, "cbuffer_layout_hash", "cbufferLayoutHash", "CBufferLayoutHash");
            string resSig = GetStrAny(root, "resource_signature", "resourceSignature", "ResourceSignature");
            string fingerprint = GetStrAny(root, "fingerprint", "Fingerprint");

            var shaderId = $"{stagePrefix}_{hash}";

            // If fingerprint isn't present, derive a stable one from the minimal structural fields.
            // IMPORTANT: do NOT include 'hash' in this key; the whole point is that multiple shader
            // variants with identical structure cluster together.
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                fingerprint = ComputeFingerprint(stage, instructionCount, textureCount, cubeCount, cbufHash, resSig);
            }

            shaderRecords.Add(new ShaderRecord(
                ShaderId: shaderId,
                Stage: stage,
                Hash: hash,
                InstructionCount: instructionCount,
                TextureCount: textureCount,
                CubeCount: cubeCount,
                CBufferLayoutHash: cbufHash,
                ResourceSignature: resSig,
                Fingerprint: fingerprint
            ));
        }

        return shaderRecords
            .OrderBy(s => s.Stage, StringComparer.Ordinal)
            .ThenBy(s => s.Hash, StringComparer.Ordinal)
            .ToList();

        static int GetIntAny(JsonElement root, params string[] props)
        {
            foreach (var prop in props)
            {
                if (!root.TryGetProperty(prop, out var v))
                    continue;
                if (v.ValueKind == JsonValueKind.Number)
                    return v.GetInt32();
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n))
                    return n;
            }
            return 0;
        }

        static string GetStrAny(JsonElement root, params string[] props)
        {
            foreach (var prop in props)
            {
                if (!root.TryGetProperty(prop, out var v))
                    continue;
                if (v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? "";
            }
            return "";
        }

        static string ComputeFingerprint(
            string stage,
            int instructionCount,
            int textureCount,
            int cubeCount,
            string cbufHash,
            string resSig)
        {
            var payload = $"{stage}|{instructionCount}|{textureCount}|{cubeCount}|{cbufHash}|{resSig}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            var hash = SHA1.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

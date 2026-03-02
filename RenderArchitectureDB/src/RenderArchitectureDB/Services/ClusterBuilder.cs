
using RenderArchitectureDB.Model;
using RenderArchitectureDB.Util;
using System.Security.Cryptography;
using System.Text;

namespace RenderArchitectureDB.Services;

public static class ClusterBuilder
{
    public sealed record Cluster(
        string ClusterId,
        string Fingerprint,
        List<string> ShaderIds,
        string InstructionBucket
    );

    public static List<Cluster> BuildClusters(List<ShaderRecord> shaders)
    {
        // group by fingerprint (or a derived fallback if the DB didn't provide one)
        var groups = shaders
            .GroupBy(s => GetKey(s), StringComparer.Ordinal)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        var clusters = new List<Cluster>();
        int idx = 1;

        foreach (var g in groups)
        {
            var shaderIds = g.Select(x => x.ShaderId).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var avg = (int)Math.Round(g.Average(x => x.InstructionCount));
            var bucket = Bucket(avg);

            clusters.Add(new Cluster(
                ClusterId: $"cluster_{idx:0000}",
                Fingerprint: g.Key,
                ShaderIds: shaderIds,
                InstructionBucket: bucket
            ));
            idx++;
        }

        return clusters;
    }

    private static string GetKey(ShaderRecord s)
    {
        if (!string.IsNullOrWhiteSpace(s.Fingerprint))
            return s.Fingerprint;

        // Derived key: stable across runs and intentionally excludes s.Hash.
        var payload = $"{s.Stage}|{s.InstructionCount}|{s.TextureCount}|{s.CubeCount}|{s.CBufferLayoutHash}|{s.ResourceSignature}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Bucket(int instructionCount)
    {
        // coarse deterministic buckets
        if (instructionCount < 200) return "0-199";
        if (instructionCount < 400) return "200-399";
        if (instructionCount < 700) return "400-699";
        if (instructionCount < 1000) return "700-999";
        return "1000+";
    }
}

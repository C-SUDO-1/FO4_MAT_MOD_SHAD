namespace RenderArchitectureDB.Util;

public static class Args
{
    public static string GetRequired(string[] args, string name)
    {
        var v = GetOptional(args, name);
        if (string.IsNullOrWhiteSpace(v))
            throw new ArgumentException($"Missing required argument: {name}");
        return v;
    }

    /// <summary>
    /// Returns the value for an argument name, or null if not present.
    /// Treats a missing value (end-of-args) or a following token that looks like another flag (starts with "--") as null.
    /// </summary>
    public static string? GetOptional(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i == args.Length - 1)
                return null;

            var candidate = args[i + 1];
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            // PowerShell can elide empty-string arguments; in that case the next token will be another flag.
            if (candidate.StartsWith("--"))
                return null;

            return candidate;
        }

        return null;
    }
}

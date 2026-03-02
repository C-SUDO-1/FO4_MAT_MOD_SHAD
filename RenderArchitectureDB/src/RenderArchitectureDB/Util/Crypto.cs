
using System.Security.Cryptography;
using System.Text;

namespace RenderArchitectureDB.Util;

public static class Crypto
{
    public static string Sha1Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

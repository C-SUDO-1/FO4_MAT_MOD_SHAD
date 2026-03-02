
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenderArchitectureDB.Util;

public static class StableJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void WriteToFile<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // IMPORTANT: Ensure stable ordering by sorting lists before calling this.
        var json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(path, json);
    }
}


namespace RenderArchitectureDB.Model;

public sealed record PermutationRecord(
    string PermId,
    string BlockType,
    int ShaderType,
    uint Spf1,
    uint Spf2,
    string? DominantClass,
    int OccurrenceCount
);

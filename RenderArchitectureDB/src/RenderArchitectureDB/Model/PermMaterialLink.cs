namespace RenderArchitectureDB.Model;

public record PermMaterialLink(
    string PermId,
    string BlockType,
    int ShaderType,
    uint Spf1,
    uint Spf2,
    string MaterialPath,
    int HitCount,
    int UniqueNifCount
);


namespace RenderArchitectureDB.Model;

public sealed record ShaderRecord(
    string ShaderId,
    string Stage,
    string Hash,
    int InstructionCount,
    int TextureCount,
    int CubeCount,
    string CBufferLayoutHash,
    string ResourceSignature,
    string Fingerprint
);

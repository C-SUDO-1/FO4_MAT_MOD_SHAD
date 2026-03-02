
namespace RenderArchitectureDB.Model;

public sealed record PermShaderLink(
    string PermId,
    string ShaderId,
    string Stage,
    int EvidenceCount,
    double ConfidenceScore,
    string MappingType
);

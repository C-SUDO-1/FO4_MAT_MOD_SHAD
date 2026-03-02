
namespace RenderArchitectureDB.Model;

public sealed record SessionObservation(
    string SessionName,
    string Stage,
    string ShaderId,
    long FirstSeenUnixMs
);

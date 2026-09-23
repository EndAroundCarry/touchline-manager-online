namespace FootballManager.Application.Ops;

public sealed record JobContext(
    Guid JobId,
    string JobType,
    string BusinessKey,
    string? PayloadJson,
    int Attempt);

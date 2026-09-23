namespace FootballManager.Application.Ops;

public sealed record JobRequest(
    string JobType,
    string BusinessKey,
    string? PayloadJson = null,
    DateTimeOffset? DueAt = null,
    int Priority = 0);

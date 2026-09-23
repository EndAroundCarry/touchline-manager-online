namespace FootballManager.Contracts.Ops;

public sealed record EnqueueNoOpJobResponse(string BusinessKey, DateTimeOffset DueAt);

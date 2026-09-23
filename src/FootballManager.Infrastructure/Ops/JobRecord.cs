namespace FootballManager.Infrastructure.Ops;

public static class JobStatus
{
    public const string Pending = "pending";
    public const string Leased = "leased";
    public const string Succeeded = "succeeded";
    public const string Dead = "dead";
}

public sealed class JobRecord
{
    public Guid Id { get; set; }

    public string JobType { get; set; } = string.Empty;

    public string BusinessKey { get; set; } = string.Empty;

    public string? PayloadJson { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public int Priority { get; set; }

    public string Status { get; set; } = JobStatus.Pending;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; }

    public Guid? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Version { get; set; }
}

namespace FootballManager.Worker.Jobs;

public sealed record JobWorkerOptions(
    TimeSpan LeaseDuration,
    TimeSpan PollInterval,
    int RetryBaseSeconds,
    int RetryMaxSeconds);

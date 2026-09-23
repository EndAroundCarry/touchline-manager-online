using Microsoft.Extensions.Logging;

namespace FootballManager.Application.Ops;

public sealed class NoOpJobHandler(ILogger<NoOpJobHandler> logger) : IJobHandler
{
    public const string Type = "ops.noop";

    public string JobType => Type;

    public Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        logger.LogInformation(
            "Handled no-op job {JobId} (business key {BusinessKey}, attempt {Attempt})",
            context.JobId,
            context.BusinessKey,
            context.Attempt);
        return Task.CompletedTask;
    }
}

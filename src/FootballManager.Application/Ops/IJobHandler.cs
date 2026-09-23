namespace FootballManager.Application.Ops;

public interface IJobHandler
{
    string JobType { get; }

    Task HandleAsync(JobContext context, CancellationToken cancellationToken);
}

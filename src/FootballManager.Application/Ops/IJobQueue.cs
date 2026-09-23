namespace FootballManager.Application.Ops;

public interface IJobQueue
{
    Task<Guid> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default);
}

using TouchlineManager.Application.Abstractions.Jobs;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Resolves a job type to its handler.
/// </summary>
/// <remarks>
/// Two handlers claiming the same job type is a configuration bug that would otherwise surface
/// as a silently ignored job, so construction fails loudly instead.
/// </remarks>
public sealed class JobHandlerRegistry
{
    private readonly Dictionary<string, IJobHandler> _handlers;

    /// <summary>Initializes the registry from every registered handler.</summary>
    /// <param name="handlers">Handlers discovered from dependency injection.</param>
    /// <exception cref="ArgumentException">Thrown when a job type is claimed twice.</exception>
    public JobHandlerRegistry(IEnumerable<IJobHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        _handlers = new Dictionary<string, IJobHandler>(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            if (!_handlers.TryAdd(handler.JobType, handler))
            {
                throw new ArgumentException(
                    $"More than one job handler is registered for job type '{handler.JobType}'.",
                    nameof(handlers));
            }
        }
    }

    /// <summary>Gets the number of registered handlers.</summary>
    public int Count => _handlers.Count;

    /// <summary>Gets every registered job type.</summary>
    public IReadOnlyCollection<string> JobTypes => _handlers.Keys;

    /// <summary>Tries to resolve the handler for a job type.</summary>
    public bool TryGet(string jobType, out IJobHandler handler) => _handlers.TryGetValue(jobType, out handler!);
}

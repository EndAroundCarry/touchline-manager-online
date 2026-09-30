using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Ops;

namespace TouchlineManager.Infrastructure.Time;

/// <summary>
/// Keeps a host's <see cref="SteppedClock"/> in step with the stored instant (ADR-0049, `TIME-6`).
/// </summary>
/// <remarks>
/// <para>
/// The stepped instant lives in the database because the API and the worker are separate processes and both
/// must answer "is this deadline due" against the same "now". The advance endpoint writes through the
/// worker; this poller is how the other host notices the change. One second of staleness is deliberate: it
/// bounds the work a step costs each host while keeping the toolbar's date and every request-time read fresh
/// enough that a person stepping a season never sees a stale day.
/// </para>
/// <para>
/// It only runs where a stepped clock is in force. In real time and compressed modes it is never registered,
/// so no host polls for a clock it does not read.
/// </para>
/// </remarks>
internal sealed partial class GameClockPoller : BackgroundService
{
    /// <summary>How often a host re-reads the stored instant.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly IGameClockStore _store;
    private readonly ILogger<GameClockPoller> _logger;

    /// <summary>Initializes the poller.</summary>
    public GameClockPoller(IGameClockStore store, ILogger<GameClockPoller> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _store.ReadAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: the next host will read the same row.
        }
        catch (Exception exception)
        {
            // A database that is briefly unavailable must not kill the poller: the next tick retries, and
            // until it succeeds the host keeps the last instant it read.
            LogFailed(exception);
        }
    }

    [LoggerMessage(
        EventId = 3910,
        Level = LogLevel.Warning,
        Message = "The stepped game clock could not be refreshed; the host keeps its last observed instant.")]
    private partial void LogFailed(Exception exception);
}

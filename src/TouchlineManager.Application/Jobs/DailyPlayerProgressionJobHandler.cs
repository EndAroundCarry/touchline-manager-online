using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Squad;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Runs the deterministic daily player progression (master plan §7.2; `TRN-3`, `TRN-9`).
/// </summary>
/// <remarks>
/// <para>
/// The handler is a thin shell over <see cref="RunDailyProgression"/>: it resolves the day the run is for
/// from the job's payload, calls the use case, and logs the counts. The use case is idempotent for a day,
/// so the queue's at-least-once delivery cannot develop a player twice.
/// </para>
/// <para>
/// A materializer enqueues the day's job; the handler never schedules its successor, because ADR-0003 keeps
/// the database row, not a timer, as the deadline. If the worker is down at the boundary the row waits and
/// runs late rather than being skipped.
/// </para>
/// </remarks>
public sealed partial class DailyPlayerProgressionJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = "squad.daily-progression";

    private readonly RunDailyProgression _progression;
    private readonly IClock _clock;
    private readonly ILogger<DailyPlayerProgressionJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public DailyPlayerProgressionJobHandler(
        RunDailyProgression progression,
        IClock clock,
        ILogger<DailyPlayerProgressionJobHandler> logger)
    {
        _progression = progression;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <summary>Builds the payload that names the day a job is for.</summary>
    /// <param name="day">The day to progress.</param>
    public static string PayloadFor(DateOnly day) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"date":"{{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}}"}""");

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var day = ResolveDay(job.PayloadJson);
        var result = await _progression.ExecuteAsync(day, cancellationToken);

        LogProgressed(day, result.Clubs, result.Players);
    }

    /// <summary>
    /// Reads the day from the payload, falling back to the current UTC date when none is present.
    /// </summary>
    /// <remarks>
    /// The fallback exists so a job enqueued without a payload still does something sensible rather than
    /// failing; a malformed payload is a programming error in the enqueuer, not a reason to dead-letter.
    /// </remarks>
    private DateOnly ResolveDay(string payloadJson)
    {
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var payload = JsonDocument.Parse(payloadJson);

                if (payload.RootElement.ValueKind == JsonValueKind.Object
                    && payload.RootElement.TryGetProperty("date", out var date)
                    && DateOnly.TryParse(
                        date.GetString(),
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return parsed;
                }
            }
            catch (JsonException exception)
            {
                LogUnreadablePayload(exception);
            }
        }

        return DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Ran daily progression for {Day}: {ClubCount} clubs, {PlayerCount} players.")]
    private partial void LogProgressed(DateOnly day, int clubCount, int playerCount);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "The daily progression job carried an unreadable payload; falling back to the current date.")]
    private partial void LogUnreadablePayload(JsonException exception);
}

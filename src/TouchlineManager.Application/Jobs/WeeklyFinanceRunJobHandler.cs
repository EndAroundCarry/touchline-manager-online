using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Finance;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Runs the weekly finance settlement (`CON-2`, `FIN-4`, `FIN-7`, `FIN-9`).
/// </summary>
/// <remarks>
/// <para>
/// A thin shell over <see cref="RunWeeklyFinance"/>: it resolves the week the run is for from the job's
/// payload, calls the use case, and logs the counts. The use case is idempotent for a week, so the queue's
/// at-least-once delivery cannot charge a club twice.
/// </para>
/// <para>
/// A materialiser enqueues the week's job; the handler never schedules its successor, because ADR-0003 keeps
/// the database row, not a timer, as the deadline. If the worker is down at the boundary the row waits and
/// runs late rather than being skipped.
/// </para>
/// </remarks>
public sealed partial class WeeklyFinanceRunJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = "finance.weekly-run";

    private readonly RunWeeklyFinance _run;
    private readonly IClock _clock;
    private readonly ILogger<WeeklyFinanceRunJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public WeeklyFinanceRunJobHandler(
        RunWeeklyFinance run,
        IClock clock,
        ILogger<WeeklyFinanceRunJobHandler> logger)
    {
        _run = run;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <summary>Builds the payload that names the week a job is for.</summary>
    /// <param name="weekOf">The week's boundary date.</param>
    public static string PayloadFor(DateOnly weekOf) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"date":"{{weekOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}}"}""");

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var weekOf = ResolveWeek(job.PayloadJson);
        var result = await _run.ExecuteAsync(weekOf, cancellationToken);

        LogSettled(weekOf, result.Clubs, result.Skipped, result.Posted, result.Grants, result.GrantedMinor);
    }

    /// <summary>
    /// Reads the week from the payload, falling back to the current UTC date when none is present.
    /// </summary>
    /// <remarks>
    /// The fallback mirrors the daily progression handler's: a job enqueued without a payload still does
    /// something sensible rather than failing, and a malformed payload is a programming error in the
    /// enqueuer rather than a reason to dead-letter.
    /// </remarks>
    private DateOnly ResolveWeek(string payloadJson)
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
        EventId = 3310,
        Level = LogLevel.Information,
        Message = "Ran the weekly finance run for {WeekOf}: {ClubCount} clubs, {SkippedCount} already settled, {EntryCount} entries, {GrantCount} grants totalling {GrantedMinor} minor units.")]
    private partial void LogSettled(
        DateOnly weekOf,
        int clubCount,
        int skippedCount,
        int entryCount,
        int grantCount,
        long grantedMinor);

    [LoggerMessage(
        EventId = 3311,
        Level = LogLevel.Warning,
        Message = "The weekly finance job carried an unreadable payload; falling back to the current date.")]
    private partial void LogUnreadablePayload(JsonException exception);
}

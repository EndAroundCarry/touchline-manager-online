using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Infrastructure.Finance;

/// <summary>
/// Materialises the weekly finance run (`CON-2`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The materialiser's job is to make the week's row exist, so the row's <c>due_at</c> is the deadline and the
/// worker holds no deadline authority itself (ADR-0003). If the worker is down at the Sunday boundary the
/// job waits and runs late rather than being skipped, and a restart re-derives the same week from the clock
/// instead of remembering where it was.
/// </para>
/// <para>
/// Enqueue is idempotent on the business key, so running this every few minutes inserts nothing after the
/// first pass. The rows are never deleted, so a week already settled is simply not re-inserted, and the
/// ledger's own correlation keys cover the rest.
/// </para>
/// </remarks>
internal sealed partial class WeeklyFinanceScheduler : BackgroundService, IJobMaterializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly FinanceOptions _options;
    private readonly ILogger<WeeklyFinanceScheduler> _logger;

    /// <summary>Initializes the scheduler.</summary>
    public WeeklyFinanceScheduler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<FinanceOptions> options,
        ILogger<WeeklyFinanceScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableWeeklyRun)
        {
            LogDisabled();

            return;
        }

        LogStarted(_options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaterializeAsync(_clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed materialisation must not kill the loop: the next pass re-derives the same week.
                LogFailed(exception);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <inheritdoc />
    public async Task MaterializeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_options.EnableWeeklyRun)
        {
            return;
        }

        var due = MostRecentBoundary(now);
        var next = due.AddDays(7);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

        var inserted = await EnsureAsync(queue, due, cancellationToken);
        inserted |= await EnsureAsync(queue, next, cancellationToken);

        if (inserted)
        {
            LogScheduled(due, next);
        }
    }

    /// <summary>Gets the most recent weekly boundary at or before now (`CON-2`).</summary>
    /// <remarks>
    /// The boundary is Sunday at <see cref="WorldRuleSet.WeeklyFinanceUtc"/>, the last of the three kickoff
    /// days. Walking back to Sunday rather than assuming a seven-day grid keeps the boundary stable across a
    /// restart, because it is derived from the date rather than from remembered state.
    /// </remarks>
    private static DateTimeOffset MostRecentBoundary(DateTimeOffset now)
    {
        var boundary = new DateTimeOffset(
            now.UtcDateTime.Date + WorldRuleSet.WeeklyFinanceUtc.ToTimeSpan(),
            TimeSpan.Zero);

        if (now < boundary)
        {
            boundary = boundary.AddDays(-1);
        }

        while (boundary.DayOfWeek != DayOfWeek.Sunday)
        {
            boundary = boundary.AddDays(-1);
        }

        return boundary;
    }

    private static async Task<bool> EnsureAsync(
        IJobQueue queue,
        DateTimeOffset boundary,
        CancellationToken cancellationToken)
    {
        var week = DateOnly.FromDateTime(boundary.UtcDateTime);

        return await queue.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = WeeklyFinanceRunJobHandler.TypeName,
                BusinessKey = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{WeeklyFinanceRunJobHandler.TypeName}:{week:yyyy-MM-dd}"),
                DueAt = boundary,
                PayloadJson = WeeklyFinanceRunJobHandler.PayloadFor(week),
            },
            cancellationToken);
    }

    [LoggerMessage(
        EventId = 3400,
        Level = LogLevel.Information,
        Message = "The weekly finance run is disabled; the scheduler is idle (CON-2).")]
    private partial void LogDisabled();

    [LoggerMessage(
        EventId = 3401,
        Level = LogLevel.Information,
        Message = "The weekly finance scheduler started, checking every {IntervalSeconds} seconds.")]
    private partial void LogStarted(int intervalSeconds);

    [LoggerMessage(
        EventId = 3402,
        Level = LogLevel.Information,
        Message = "Materialised the weekly finance run; the most recent boundary was {Due} and the next is {Next}.")]
    private partial void LogScheduled(DateTimeOffset due, DateTimeOffset next);

    [LoggerMessage(
        EventId = 3403,
        Level = LogLevel.Error,
        Message = "The weekly finance scheduler failed to materialise the week's job.")]
    private partial void LogFailed(Exception exception);
}

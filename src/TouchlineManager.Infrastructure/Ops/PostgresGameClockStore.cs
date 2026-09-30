using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Configurations;
using TouchlineManager.Infrastructure.Time;

namespace TouchlineManager.Infrastructure.Ops;

/// <summary>
/// The stored instant a stepped game clock reads, in <c>ops.game_clock</c> (ADR-0049, `TIME-6`).
/// </summary>
/// <remarks>
/// <para>
/// A singleton, because the clock it feeds is a singleton and is injected into the worker's own singletons.
/// It reads through the scope factory rather than holding a scoped context, and it caches the instant so
/// <see cref="Current"/> never blocks: the API and the worker refresh it through
/// <see cref="GameClockPoller"/>, and the advance job writes it.
/// </para>
/// <para>
/// The row is created on first read from the configured starting instant, so a fresh world begins at a
/// chosen moment without a seeding step of its own. Only one row can exist, enforced by the table's check
/// constraint.
/// </para>
/// </remarks>
internal sealed partial class PostgresGameClockStore : IGameClockStore
{
    /// <summary>The sentinel that means "no instant read or written yet".</summary>
    private const long Unset = long.MinValue;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClockOptions _options;
    private readonly ILogger<PostgresGameClockStore> _logger;

    /// <summary>The last observed instant, as UTC ticks, so reads are an atomic load.</summary>
    private long _currentTicks = Unset;

    /// <summary>Initializes the store.</summary>
    public PostgresGameClockStore(
        IServiceScopeFactory scopeFactory,
        IOptions<ClockOptions> options,
        ILogger<PostgresGameClockStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public DateTimeOffset Current
    {
        get
        {
            var ticks = Interlocked.Read(ref _currentTicks);

            return ticks == Unset
                ? _options.InitialNowUtc ?? DateTimeOffset.UtcNow
                : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset> ReadAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var stored = await dbContext.GameClocks
            .Where(row => row.Id == OpsGameClockConfiguration.SingleRowId)
            .Select(row => (DateTimeOffset?)row.GameNow)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var seed = _options.InitialNowUtc ?? DateTimeOffset.UtcNow;

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 insert into ops.game_clock (id, game_now, updated_at, version)
                 values ({OpsGameClockConfiguration.SingleRowId}, {seed}, {seed}, 1)
                 on conflict (id) do nothing
                 """,
                cancellationToken);

            stored = await dbContext.GameClocks
                .Where(row => row.Id == OpsGameClockConfiguration.SingleRowId)
                .Select(row => (DateTimeOffset?)row.GameNow)
                .FirstOrDefaultAsync(cancellationToken) ?? seed;

            LogInitialized(stored.Value);
        }

        Interlocked.Exchange(ref _currentTicks, stored.Value.UtcTicks);

        return stored.Value;
    }

    /// <inheritdoc />
    public async Task WriteAsync(DateTimeOffset gameNow, CancellationToken cancellationToken)
    {
        var utc = gameNow.ToUniversalTime();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             insert into ops.game_clock (id, game_now, updated_at, version)
             values ({OpsGameClockConfiguration.SingleRowId}, {utc}, {utc}, 1)
             on conflict (id) do update
                 set game_now = excluded.game_now,
                     updated_at = excluded.updated_at,
                     version = ops.game_clock.version + 1
             """,
            cancellationToken);

        Interlocked.Exchange(ref _currentTicks, utc.UtcTicks);

        LogAdvanced(utc);
    }

    [LoggerMessage(
        EventId = 3900,
        Level = LogLevel.Information,
        Message = "The stepped game clock was created at {GameNow} (ADR-0049).")]
    private partial void LogInitialized(DateTimeOffset gameNow);

    [LoggerMessage(
        EventId = 3901,
        Level = LogLevel.Information,
        Message = "The stepped game clock advanced to {GameNow} (ADR-0049).")]
    private partial void LogAdvanced(DateTimeOffset gameNow);
}

using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.World;

namespace TouchlineManager.Application.Ops;

/// <summary>The state of the stepped game clock, for the non-production toolbar (ADR-0049).</summary>
/// <param name="GameNow">The current game instant.</param>
/// <param name="NextMatchdayAt">The next round's kickoff, or null when the season has no round left to play.</param>
/// <param name="SeasonEndsAt">
/// The end of the active season when no round is left, which is when its rollover runs and the toolbar offers to end the season; null
/// while a round is still to play or when the season has closed.
/// </param>
public sealed record GameClockStatus(DateTimeOffset GameNow, DateTimeOffset? NextMatchdayAt, DateTimeOffset? SeasonEndsAt);

/// <summary>
/// Reads the stepped game clock and the next round, so the toolbar can show the date and enable its buttons
/// (ADR-0049).
/// </summary>
/// <remarks>
/// It reads the stored instant rather than <c>IClock</c>, so the date shown is authoritative rather than a
/// poller's copy. It is reachable only from a development-flagged endpoint and is never mapped in production
/// (§17.12).
/// </remarks>
public sealed class GetGameClockStatus
{
    /// <summary>How far ahead a round is looked for.</summary>
    private static readonly TimeSpan MatchdayHorizon = TimeSpan.FromDays(400);

    private readonly IGameClockStore _clockStore;
    private readonly IMatchdayRepository _matchdays;
    private readonly IWorldRepository _world;

    /// <summary>Initializes the read.</summary>
    public GetGameClockStatus(IGameClockStore clockStore, IMatchdayRepository matchdays, IWorldRepository world)
    {
        _clockStore = clockStore;
        _matchdays = matchdays;
        _world = world;
    }

    /// <summary>Reads the current instant and the next round's kickoff.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GameClockStatus> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = await _clockStore.ReadAsync(cancellationToken);

        var upcoming = await _matchdays.ListPendingLockingBetweenAsync(
            now,
            now.Add(MatchdayHorizon),
            cancellationToken);

        var nextMatchdayAt = upcoming.Count > 0 ? upcoming[0].KickoffAt : (DateTimeOffset?)null;

        // With no round left the season can only be closed: say when, so the toolbar can offer the step.
        var seasonEndsAt = nextMatchdayAt is null
            ? (await ClockSeason.FindActiveAsync(_world, cancellationToken))?.EndsAt
            : null;

        return new GameClockStatus(now, nextMatchdayAt, seasonEndsAt);
    }
}

using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Ops;

namespace TouchlineManager.Application.Ops;

/// <summary>The state of the stepped game clock, for the non-production toolbar (ADR-0049).</summary>
/// <param name="GameNow">The current game instant.</param>
/// <param name="NextMatchdayAt">The next round's kickoff, or null when the season has no round left to play.</param>
public sealed record GameClockStatus(DateTimeOffset GameNow, DateTimeOffset? NextMatchdayAt);

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

    /// <summary>Initializes the read.</summary>
    public GetGameClockStatus(IGameClockStore clockStore, IMatchdayRepository matchdays)
    {
        _clockStore = clockStore;
        _matchdays = matchdays;
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

        return new GameClockStatus(now, nextMatchdayAt);
    }
}

using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Domain.Match;

namespace TouchlineManager.Application.Match;

/// <summary>What one match did to one player (`DIS-1`, `DIS-2`, `DIS-4`).</summary>
/// <param name="FixtureId">The fixture the player played in.</param>
/// <param name="ClubId">The club the player played for.</param>
/// <param name="PlayerId">The affected player.</param>
/// <param name="YellowCards">The yellows shown, counting a second yellow as the booking it was.</param>
/// <param name="RedCards">The sending-offs, counting a second yellow as the red it became.</param>
/// <param name="AbsenceFixtures">The fixtures an injury rules the player out for, or zero when uninjured.</param>
public sealed record MatchPlayerEffect(
    Guid FixtureId,
    Guid ClubId,
    Guid PlayerId,
    int YellowCards,
    int RedCards,
    int AbsenceFixtures);

/// <summary>
/// Turns a matchday's card and injury events into one effect per affected player (`DIS-1`, `DIS-2`,
/// `DIS-4`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the event stream, like the engine's own statistics: the effect on a player is a count
/// over the events that name them, so it cannot drift from the match it describes and a re-derivation
/// reproduces it. The counts mirror the engine's own reconciliation rule (`MAT-5`) — a second yellow is
/// both the booking it was and the sending-off it became — which is what makes the discipline record agree
/// with the match statistics a manager can read.
/// </para>
/// <para>
/// The calculator decides nothing about thresholds or suspensions: it says what happened, and the
/// publication applies the rule set to it. That keeps the "how many yellows is a ban" question in one
/// place — the rules — rather than in a counting function.
/// </para>
/// </remarks>
public static class MatchEffectsCalculator
{
    /// <summary>Aggregates a matchday's effect events per player.</summary>
    /// <param name="events">The card and injury events, in any order.</param>
    /// <returns>One effect per player who was booked, sent off, or injured, in a stable order.</returns>
    public static IReadOnlyList<MatchPlayerEffect> Calculate(IEnumerable<MatchEffectEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return
        [
            .. events
                .GroupBy(matchEvent => (matchEvent.FixtureId, matchEvent.ClubId, matchEvent.PlayerId))
                .Select(group => new MatchPlayerEffect(
                    group.Key.FixtureId,
                    group.Key.ClubId,
                    group.Key.PlayerId,
                    group.Count(IsBooking),
                    group.Count(IsSendingOff),
                    group.Max(matchEvent => matchEvent.AbsenceFixtures)))
                .OrderBy(effect => effect.FixtureId)
                .ThenBy(effect => effect.ClubId)
                .ThenBy(effect => effect.PlayerId),
        ];
    }

    private static bool IsBooking(MatchEffectEvent matchEvent) =>
        matchEvent.Type is MatchEventType.YellowCard or MatchEventType.SecondYellowCard;

    private static bool IsSendingOff(MatchEffectEvent matchEvent) =>
        matchEvent.Type is MatchEventType.RedCard or MatchEventType.SecondYellowCard;
}

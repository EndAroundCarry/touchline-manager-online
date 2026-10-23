using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Competition;

/// <summary>One match of the promotion bracket before it is stored as a <see cref="PlayoffTie"/> (`PR-4`).</summary>
/// <param name="Stage">Which match of the bracket this is.</param>
/// <param name="Slot">The kickoff slot after matchday 30 that the match is planned for.</param>
/// <param name="ParentDivisionId">The higher league of the pair.</param>
/// <param name="FeederDivisionId">The lower league of the pair.</param>
/// <param name="HostDivisionId">The league whose division-season hosts the match: the lower league for a semi-final, the higher for the final.</param>
/// <param name="HomeClubId">The home club.</param>
/// <param name="HomeDivisionId">The home club's league this season.</param>
/// <param name="HomePlace">The home club's final table place.</param>
/// <param name="AwayClubId">The away club, or <see langword="null"/> while it waits for a semi-final winner.</param>
/// <param name="AwayDivisionId">The away club's league, or <see langword="null"/> while it is not known.</param>
/// <param name="AwayPlace">The away club's final table place, or <see langword="null"/> while it is not known.</param>
public sealed record PlayoffPairing(
    PlayoffStage Stage,
    int Slot,
    Guid ParentDivisionId,
    Guid FeederDivisionId,
    Guid HostDivisionId,
    Guid HomeClubId,
    Guid HomeDivisionId,
    int HomePlace,
    Guid? AwayClubId,
    Guid? AwayDivisionId,
    int? AwayPlace);

/// <summary>
/// Draws the promotion play-off bracket from the leagues' final tables (`PR-4`).
/// </summary>
/// <remarks>
/// <para>
/// A higher league with exactly one lower league feeding it gets two matches: a semi-final, the feeder's 3rd at home to
/// its 4th, and a final, the higher league's 14th at home to the semi-final winner. The final's away side is not known
/// until the semi-final is played, so its pairing leaves the away club empty.
/// </para>
/// <para>
/// A higher league with no feeder gets nothing, and so does one with two or more: that format belongs to the growing
/// pyramid's own milestone, and <see cref="PromotionRelegation.Validate"/> reports it rather than drawing half a bracket.
/// </para>
/// </remarks>
public static class PlayoffBracket
{
    /// <summary>Draws the bracket of every pair of leagues that has one feeder.</summary>
    /// <param name="leagues">The leagues of a country with their final order.</param>
    /// <returns>For each pair, the semi-final then the final, pairs in ascending tier order.</returns>
    public static IReadOnlyList<PlayoffPairing> Draw(IReadOnlyList<LeagueStandings> leagues)
    {
        ArgumentNullException.ThrowIfNull(leagues);

        var pairings = new List<PlayoffPairing>();

        foreach (var parent in leagues.OrderBy(league => league.Tier).ThenBy(league => league.League))
        {
            var feeders = leagues.Where(league => league.ParentDivisionId == parent.DivisionId).ToList();

            if (feeders.Count != 1)
            {
                continue;
            }

            var feeder = feeders[0];

            if (feeder.RankedClubIds.Count < WorldRuleSet.PlayoffSemiAwayPlace
                || parent.RankedClubIds.Count < WorldRuleSet.PlayoffHolderPlace)
            {
                throw new ArgumentException(
                    $"Leagues {parent.DivisionId} and {feeder.DivisionId} are too small for a play-off (PR-4).",
                    nameof(leagues));
            }

            var semiHome = feeder.RankedClubIds[WorldRuleSet.PlayoffSemiHomePlace - 1];
            var semiAway = feeder.RankedClubIds[WorldRuleSet.PlayoffSemiAwayPlace - 1];
            var holder = parent.RankedClubIds[WorldRuleSet.PlayoffHolderPlace - 1];

            pairings.Add(new PlayoffPairing(
                PlayoffStage.SemiFinal,
                WorldRuleSet.PlayoffSemiSlot,
                parent.DivisionId,
                feeder.DivisionId,
                feeder.DivisionId,
                semiHome,
                feeder.DivisionId,
                WorldRuleSet.PlayoffSemiHomePlace,
                semiAway,
                feeder.DivisionId,
                WorldRuleSet.PlayoffSemiAwayPlace));

            pairings.Add(new PlayoffPairing(
                PlayoffStage.Final,
                WorldRuleSet.PlayoffFinalSlot,
                parent.DivisionId,
                feeder.DivisionId,
                parent.DivisionId,
                holder,
                parent.DivisionId,
                WorldRuleSet.PlayoffHolderPlace,
                null,
                null,
                null));
        }

        return pairings;
    }
}

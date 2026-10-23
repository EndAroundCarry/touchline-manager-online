using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Competition;

/// <summary>How a club earned its movement (`PR-1`).</summary>
public enum MovementRoute
{
    /// <summary>By its final table place, or by staying where it was.</summary>
    Direct = 0,

    /// <summary>By winning or losing the promotion play-off final.</summary>
    Playoff = 1,
}

/// <summary>One decided match of a season's promotion play-off bracket, as the movement rule reads it (`PR-4`).</summary>
/// <param name="ParentDivisionId">The higher league of the pair.</param>
/// <param name="FeederDivisionId">The lower league of the pair.</param>
/// <param name="Stage">Which match of the bracket it was.</param>
/// <param name="WinnerClubId">The club that went through.</param>
/// <param name="LoserClubId">The club that was out.</param>
public sealed record PlayoffOutcome(
    Guid ParentDivisionId,
    Guid FeederDivisionId,
    PlayoffStage Stage,
    Guid WinnerClubId,
    Guid LoserClubId);

/// <summary>One club's movement between two seasons (`PR-3`).</summary>
/// <remarks>
/// Movement applies to the <em>club</em>, not to the manager, so a human manager keeps their club and
/// travels with it (`PR-3`). A club that neither goes up nor down still carries a movement record with
/// <see cref="FromDivisionId"/> equal to <see cref="ToDivisionId"/>, so every club has exactly one placement.
/// </remarks>
/// <param name="ClubId">The club.</param>
/// <param name="FromTier">The tier it played in.</param>
/// <param name="ToTier">The tier it plays in next.</param>
/// <param name="IsPromoted">Whether it went up.</param>
/// <param name="IsRelegated">Whether it went down.</param>
/// <param name="FromDivisionId">The league it played in.</param>
/// <param name="ToDivisionId">The league it plays in next.</param>
/// <param name="Route">Whether the table or the play-off moved it.</param>
public sealed record ClubMovement(
    Guid ClubId,
    int FromTier,
    int ToTier,
    bool IsPromoted,
    bool IsRelegated,
    Guid FromDivisionId,
    Guid ToDivisionId,
    MovementRoute Route)
{
    /// <summary>Gets a value indicating whether the club stays in its tier.</summary>
    public bool IsStationary => !IsPromoted && !IsRelegated;
}

/// <summary>
/// The promotion and relegation rules as one pure function (`PR-1`…`PR-10`).
/// </summary>
/// <remarks>
/// <para>
/// Between a lower league and the league above it that it feeds, the lower league's 1st and 2nd go up and the upper
/// league's 15th and 16th go down. The lower league's 3rd and 4th then play a semi-final and the winner plays the upper
/// league's 14th (<see cref="PlayoffBracket"/>); if the challenger wins the final it goes up and the 14th goes down, and
/// otherwise nobody else moves. That is two or three clubs each way, and always sixteen in every league.
/// </para>
/// <para>
/// The top league promotes nobody and a league with no feeder relegates nobody (`PR-2`, `PR-9`, `PR-10`); both fall out
/// of the pairs rather than needing their own branch. A pair that has no play-off outcome because it had no time to play
/// one moves its direct places only. A pair whose semi-final was played but whose final was not is an error, never a
/// silent direct-only season.
/// </para>
/// <para>
/// Nothing here reads a clock, a database, or a random source. The input is the finalized ordering the standings
/// projection already produced and the play-off results the season stored, so the movement a finished season earns is
/// reproducible from what was stored.
/// </para>
/// </remarks>
public static class PromotionRelegation
{
    /// <summary>The version of the movement rule, so a change in how clubs move is a named change.</summary>
    public const string Version = "promotion-relegation-v2";

    /// <summary>Lists what makes a set of leagues unfit for the movement rule; empty when it is fit.</summary>
    /// <param name="leagues">A country's active leagues, each with its final order.</param>
    public static IReadOnlyList<string> Validate(IReadOnlyList<LeagueStandings> leagues)
    {
        ArgumentNullException.ThrowIfNull(leagues);

        var problems = new List<string>();
        var byDivision = new Dictionary<Guid, LeagueStandings>();

        foreach (var league in leagues)
        {
            if (!byDivision.TryAdd(league.DivisionId, league))
            {
                problems.Add($"League {league.DivisionId} appears twice.");
            }
        }

        foreach (var tier in leagues.GroupBy(league => league.Tier).Where(group => group.Count() > 1))
        {
            problems.Add($"Tier {tier.Key} holds {tier.Count()} leagues; a tier holds one until the two-feeder play-off exists.");
        }

        foreach (var parent in leagues.GroupBy(league => league.ParentDivisionId).Where(group => group.Key is not null && group.Count() > 1))
        {
            problems.Add($"League {parent.Key} has {parent.Count()} feeder leagues; the play-off covers one.");
        }

        foreach (var league in leagues.Where(league => league.ParentDivisionId is not null))
        {
            if (!byDivision.TryGetValue(league.ParentDivisionId!.Value, out var parent))
            {
                problems.Add($"League {league.DivisionId} feeds {league.ParentDivisionId}, which is not an active league.");
            }
            else if (parent.Tier != league.Tier - 1)
            {
                problems.Add($"League {league.DivisionId} in tier {league.Tier} feeds a league in tier {parent.Tier}.");
            }
        }

        return problems;
    }

    /// <summary>Computes every club's movement between two seasons.</summary>
    /// <param name="leagues">
    /// A country's active leagues, each with its clubs in final order. Any order is accepted; the result is the same
    /// whatever order they arrive in, because the movement is computed per pair of leagues.
    /// </param>
    /// <param name="outcomes">The decided matches of the season's play-off bracket; none for a pair that played no play-off.</param>
    /// <returns>One movement per club, in ascending destination tier then descending origin tier order.</returns>
    public static IReadOnlyList<ClubMovement> Compute(
        IReadOnlyList<LeagueStandings> leagues,
        IReadOnlyList<PlayoffOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(leagues);
        ArgumentNullException.ThrowIfNull(outcomes);

        if (leagues.Count == 0)
        {
            return [];
        }

        var problems = Validate(leagues);

        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(leagues));
        }

        var placements = new Dictionary<Guid, ClubMovement>();

        foreach (var league in leagues)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(league.Tier, 1);

            if (league.RankedClubIds.Count == 0)
            {
                throw new ArgumentException($"League {league.DivisionId} has no clubs to move.", nameof(leagues));
            }

            foreach (var clubId in league.RankedClubIds)
            {
                if (!placements.TryAdd(
                        clubId,
                        new ClubMovement(clubId, league.Tier, league.Tier, false, false, league.DivisionId, league.DivisionId, MovementRoute.Direct)))
                {
                    throw new ArgumentException(
                        $"Club {clubId} appears in more than one league; a club has exactly one placement (PR-3).",
                        nameof(leagues));
                }
            }
        }

        var pairKeys = leagues
            .Where(league => league.ParentDivisionId is not null)
            .Select(league => (Parent: league.ParentDivisionId!.Value, Feeder: league.DivisionId))
            .ToHashSet();

        foreach (var outcome in outcomes)
        {
            if (!pairKeys.Contains((outcome.ParentDivisionId, outcome.FeederDivisionId)))
            {
                throw new ArgumentException(
                    $"A play-off outcome names leagues {outcome.ParentDivisionId} and {outcome.FeederDivisionId}, which are not a pair (PR-4).",
                    nameof(outcomes));
            }
        }

        var byDivision = leagues.ToDictionary(league => league.DivisionId);

        foreach (var feeder in leagues.Where(league => league.ParentDivisionId is not null))
        {
            var parent = byDivision[feeder.ParentDivisionId!.Value];

            if (feeder.RankedClubIds.Count < WorldRuleSet.DirectPromotionPlaces
                || parent.RankedClubIds.Count < WorldRuleSet.DirectRelegationPlaces)
            {
                throw new ArgumentException(
                    $"Leagues {parent.DivisionId} and {feeder.DivisionId} do not each hold "
                    + $"{WorldRuleSet.DirectPromotionPlaces} clubs to exchange (PR-1).",
                    nameof(leagues));
            }

            foreach (var clubId in feeder.RankedClubIds.Take(WorldRuleSet.DirectPromotionPlaces))
            {
                Move(placements, clubId, feeder, parent, promoted: true, MovementRoute.Direct);
            }

            foreach (var clubId in parent.RankedClubIds.TakeLast(WorldRuleSet.DirectRelegationPlaces))
            {
                Move(placements, clubId, parent, feeder, promoted: false, MovementRoute.Direct);
            }

            ApplyPlayoff(placements, parent, feeder, outcomes);
        }

        return placements.Values
            .OrderBy(movement => movement.ToTier)
            .ThenByDescending(movement => movement.FromTier)
            .ThenBy(movement => movement.ClubId)
            .ToList();
    }

    private static void ApplyPlayoff(
        Dictionary<Guid, ClubMovement> placements,
        LeagueStandings parent,
        LeagueStandings feeder,
        IReadOnlyList<PlayoffOutcome> outcomes)
    {
        var pair = outcomes
            .Where(outcome => outcome.ParentDivisionId == parent.DivisionId && outcome.FeederDivisionId == feeder.DivisionId)
            .ToList();

        var semi = pair.SingleOrDefault(outcome => outcome.Stage == PlayoffStage.SemiFinal);
        var final = pair.SingleOrDefault(outcome => outcome.Stage == PlayoffStage.Final);

        if (semi is null && final is null)
        {
            // No bracket was played for the pair (it had no time to), so only the direct places move.
            return;
        }

        if (final is null)
        {
            throw new InvalidOperationException(
                $"The play-off between leagues {parent.DivisionId} and {feeder.DivisionId} has a semi-final but no decided final (PR-4).");
        }

        if (parent.RankedClubIds.Count < WorldRuleSet.PlayoffHolderPlace
            || feeder.RankedClubIds.Count < WorldRuleSet.PlayoffSemiAwayPlace)
        {
            throw new ArgumentException(
                $"Leagues {parent.DivisionId} and {feeder.DivisionId} are too small for a play-off (PR-4).",
                nameof(parent));
        }

        var holder = parent.RankedClubIds[WorldRuleSet.PlayoffHolderPlace - 1];
        var semiHome = feeder.RankedClubIds[WorldRuleSet.PlayoffSemiHomePlace - 1];
        var semiAway = feeder.RankedClubIds[WorldRuleSet.PlayoffSemiAwayPlace - 1];

        if (semi is not null && !IsPair(semi.WinnerClubId, semi.LoserClubId, semiHome, semiAway))
        {
            throw new InvalidOperationException(
                $"The semi-final of leagues {parent.DivisionId} and {feeder.DivisionId} was not played by the 3rd and 4th (PR-4).");
        }

        var challenger = final.WinnerClubId == holder ? final.LoserClubId : final.WinnerClubId;

        if (semi is not null && challenger != semi.WinnerClubId)
        {
            throw new InvalidOperationException(
                $"The final of leagues {parent.DivisionId} and {feeder.DivisionId} was not played by the semi-final winner (PR-4).");
        }

        if (final.WinnerClubId == holder)
        {
            if (final.LoserClubId != semiHome && final.LoserClubId != semiAway)
            {
                throw new InvalidOperationException(
                    $"The final of leagues {parent.DivisionId} and {feeder.DivisionId} was not played against the 3rd or 4th (PR-4).");
            }

            return;
        }

        if (final.LoserClubId != holder || (final.WinnerClubId != semiHome && final.WinnerClubId != semiAway))
        {
            throw new InvalidOperationException(
                $"The final of leagues {parent.DivisionId} and {feeder.DivisionId} was not played by the 14th and the 3rd or 4th (PR-4).");
        }

        Move(placements, final.WinnerClubId, feeder, parent, promoted: true, MovementRoute.Playoff);
        Move(placements, holder, parent, feeder, promoted: false, MovementRoute.Playoff);
    }

    private static bool IsPair(Guid a, Guid b, Guid x, Guid y) =>
        (a == x && b == y) || (a == y && b == x);

    private static void Move(
        Dictionary<Guid, ClubMovement> placements,
        Guid clubId,
        LeagueStandings from,
        LeagueStandings to,
        bool promoted,
        MovementRoute route)
    {
        if (!placements[clubId].IsStationary)
        {
            throw new InvalidOperationException($"Club {clubId} would move twice (PR-3).");
        }

        placements[clubId] = new ClubMovement(
            clubId,
            from.Tier,
            to.Tier,
            promoted,
            !promoted,
            from.DivisionId,
            to.DivisionId,
            route);
    }
}

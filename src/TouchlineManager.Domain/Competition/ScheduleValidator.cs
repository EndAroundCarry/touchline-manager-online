using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Competition;

/// <summary>One reason a generated schedule was rejected, as a stable code plus a detail for the log.</summary>
/// <param name="Code">The stable issue code.</param>
/// <param name="Detail">A human-readable detail naming the round or club concerned.</param>
public sealed record ScheduleIssue(string Code, string Detail);

/// <summary>Stable codes for the schedule issues (<see cref="ScheduleValidator"/>).</summary>
public static class ScheduleIssueCodes
{
    /// <summary>The list is not the length a double round-robin must be.</summary>
    public const string RoundCountIncorrect = "SCHEDULE_ROUND_COUNT_INCORRECT";

    /// <summary>A round does not contain every club exactly once.</summary>
    public const string RoundIncomplete = "SCHEDULE_ROUND_INCOMPLETE";

    /// <summary>The same ordered pairing appears more than once.</summary>
    public const string PairingRepeated = "SCHEDULE_PAIRING_REPEATED";

    /// <summary>A pairing is not reciprocated: the pair does not meet once at each venue.</summary>
    public const string PairingNotReciprocated = "SCHEDULE_PAIRING_NOT_RECIPROCATED";

    /// <summary>A club's home and away counts are not equal.</summary>
    public const string HomeAwayUnbalanced = "SCHEDULE_HOME_AWAY_UNBALANCED";

    /// <summary>A club has a longer run of games at one venue than the rule allows.</summary>
    public const string StreakTooLong = "SCHEDULE_STREAK_TOO_LONG";
}

/// <summary>
/// Checks a generated schedule against the properties `CAL-9` requires, by name.
/// </summary>
/// <remarks>
/// The generator is built so these properties hold by construction, and this exists precisely because
/// "by construction" is an argument rather than evidence: it is the assertion the seeder runs before it
/// writes a schedule into the database, and the property the tests check across sizes and seeds. A
/// malformed schedule that reached the database would be a season that quietly could not be played
/// correctly, which is far worse than a failed generation.
/// </remarks>
public static class ScheduleValidator
{
    /// <summary>Validates a generated schedule, returning every issue found.</summary>
    /// <param name="clubIds">The clubs the schedule was generated for.</param>
    /// <param name="schedule">The generated rounds, in order.</param>
    /// <returns>The issues found; empty when the schedule is valid.</returns>
    public static IReadOnlyList<ScheduleIssue> Validate(
        IReadOnlyList<Guid> clubIds,
        IReadOnlyList<ScheduleRound> schedule)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        ArgumentNullException.ThrowIfNull(schedule);

        var issues = new List<ScheduleIssue>();

        if (clubIds.Count < 2 || clubIds.Count % 2 != 0)
        {
            issues.Add(new ScheduleIssue(
                ScheduleIssueCodes.RoundCountIncorrect,
                $"A round-robin needs an even number of at least two clubs, not {clubIds.Count}."));

            return issues;
        }

        var expectedRounds = (clubIds.Count - 1) * 2;

        if (schedule.Count != expectedRounds)
        {
            issues.Add(new ScheduleIssue(
                ScheduleIssueCodes.RoundCountIncorrect,
                $"Expected {expectedRounds} rounds for {clubIds.Count} clubs, found {schedule.Count}."));
        }

        var orderedPairs = new HashSet<(Guid Home, Guid Away)>();
        var homeCounts = clubIds.ToDictionary(club => club, _ => 0);
        var awayCounts = clubIds.ToDictionary(club => club, _ => 0);

        for (var round = 0; round < schedule.Count; round++)
        {
            var declaredRound = schedule[round].RoundNumber;

            if (declaredRound != round + 1)
            {
                issues.Add(new ScheduleIssue(
                    ScheduleIssueCodes.RoundCountIncorrect,
                    $"Round at position {round + 1} is declared round {declaredRound}."));
            }

            var seen = new HashSet<Guid>();

            foreach (var pairing in schedule[round].Pairings)
            {
                if (!orderedPairs.Add((pairing.HomeClubId, pairing.AwayClubId)))
                {
                    issues.Add(new ScheduleIssue(
                        ScheduleIssueCodes.PairingRepeated,
                        $"Round {declaredRound} repeats the pairing {pairing.HomeClubId} vs {pairing.AwayClubId}."));
                }

                if (homeCounts.TryGetValue(pairing.HomeClubId, out var home))
                {
                    homeCounts[pairing.HomeClubId] = home + 1;
                }

                if (awayCounts.TryGetValue(pairing.AwayClubId, out var away))
                {
                    awayCounts[pairing.AwayClubId] = away + 1;
                }

                if (!seen.Add(pairing.HomeClubId) || !seen.Add(pairing.AwayClubId))
                {
                    issues.Add(new ScheduleIssue(
                        ScheduleIssueCodes.RoundIncomplete,
                        $"A club appears more than once in round {declaredRound}."));
                }
            }

            if (seen.Count != clubIds.Count)
            {
                issues.Add(new ScheduleIssue(
                    ScheduleIssueCodes.RoundIncomplete,
                    $"Round {declaredRound} fields {seen.Count} of {clubIds.Count} clubs."));
            }
        }

        foreach (var club in clubIds)
        {
            if (homeCounts[club] != clubIds.Count - 1 || awayCounts[club] != clubIds.Count - 1)
            {
                issues.Add(new ScheduleIssue(
                    ScheduleIssueCodes.HomeAwayUnbalanced,
                    $"Club {club} plays {homeCounts[club]} home and {awayCounts[club]} away."));
            }
        }

        foreach (var pair in Pairs(clubIds))
        {
            if (!orderedPairs.Contains(pair) || !orderedPairs.Contains((pair.Away, pair.Home)))
            {
                issues.Add(new ScheduleIssue(
                    ScheduleIssueCodes.PairingNotReciprocated,
                    $"Clubs {pair.Home} and {pair.Away} do not meet once at each venue."));
            }
        }

        issues.AddRange(StreakIssues(clubIds, schedule));

        return issues;
    }

    private static IEnumerable<(Guid Home, Guid Away)> Pairs(IReadOnlyList<Guid> clubIds)
    {
        for (var first = 0; first < clubIds.Count; first++)
        {
            for (var second = first + 1; second < clubIds.Count; second++)
            {
                yield return (clubIds[first], clubIds[second]);
            }
        }
    }

    private static IEnumerable<ScheduleIssue> StreakIssues(
        IReadOnlyList<Guid> clubIds,
        IReadOnlyList<ScheduleRound> schedule)
    {
        foreach (var club in clubIds)
        {
            var venues = new List<bool>();

            foreach (var round in schedule.OrderBy(round => round.RoundNumber))
            {
                foreach (var pairing in round.Pairings)
                {
                    if (pairing.HomeClubId == club)
                    {
                        venues.Add(true);
                    }
                    else if (pairing.AwayClubId == club)
                    {
                        venues.Add(false);
                    }
                }
            }

            var run = 1;

            for (var index = 1; index < venues.Count; index++)
            {
                run = venues[index] == venues[index - 1] ? run + 1 : 1;

                if (run > WorldRuleSet.MaxConsecutiveHomeOrAway)
                {
                    yield return new ScheduleIssue(
                        ScheduleIssueCodes.StreakTooLong,
                        $"Club {club} has {run} consecutive "
                        + (venues[index] ? "home" : "away")
                        + $" fixtures, over the limit of {WorldRuleSet.MaxConsecutiveHomeOrAway} (CAL-9).");

                    break;
                }
            }
        }
    }
}

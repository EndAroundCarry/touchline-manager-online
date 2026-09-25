using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Serialization;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Assembles a finished match into the output contract.
/// </summary>
/// <remarks>
/// Every statistic is counted from the event log rather than from a parallel set of counters. `MAT-5`
/// requires the final score to equal the goal events and the statistics to reconcile with the events, and the
/// only way to guarantee that is to have one source of them: a saved goal is then one event, not a goal
/// counter that might disagree with a shots counter.
/// </remarks>
internal static class MatchResultBuilder
{
    /// <summary>Builds the result, including its output hash.</summary>
    /// <param name="state">The finished match state.</param>
    /// <param name="inputHash">The input hash the result is bound to.</param>
    public static MatchResultV1 Build(MatchState state, string inputHash)
    {
        ArgumentNullException.ThrowIfNull(state);

        var possession = PossessionShare(state);

        var result = new MatchResultV1
        {
            EngineVersion = state.Input.EngineVersion,
            RuleSetVersion = state.Input.RuleSetVersion,
            HomeGoals = state.GoalsOf(MatchSide.Home),
            AwayGoals = state.GoalsOf(MatchSide.Away),
            Home = Statistics(state, MatchSide.Home, possession),
            Away = Statistics(state, MatchSide.Away, 10_000 - possession),
            Events = [.. state.Events],
            PlayerLines = PlayerLines(state),
            TotalMinutesPlayed = state.TotalMinutesPlayed,
            InputHash = inputHash,
            OutputHash = string.Empty,
        };

        // The output hash covers everything above, so it is computed last and written back in. Hashing a
        // result that already carried a hash would be circular.
        return result with { OutputHash = CanonicalMatchSerializer.OutputHash(result) };
    }

    private static int PossessionShare(MatchState state)
    {
        var total = state.Home.PossessionSeconds + state.Away.PossessionSeconds;

        return total <= 0
            ? 5_000
            : (int)(((long)state.Home.PossessionSeconds * 10_000) / total);
    }

    private static MatchStatisticsV1 Statistics(MatchState state, MatchSide side, int possessionBasisPoints)
    {
        int Count(EngineEventType type) =>
            state.Events.Count(matchEvent => matchEvent.Side == side && matchEvent.Type == type);

        var goals = Count(EngineEventType.Goal) + Count(EngineEventType.PenaltyGoal);
        var saves = Count(EngineEventType.ShotSaved);
        var blocked = Count(EngineEventType.ShotBlocked);
        var woodwork = Count(EngineEventType.Woodwork);
        var offTarget = Count(EngineEventType.ShotOffTarget) + Count(EngineEventType.PenaltyMissed);

        return new MatchStatisticsV1
        {
            PossessionBasisPoints = possessionBasisPoints,
            Goals = goals,
            Shots = goals + saves + blocked + woodwork + offTarget,
            ShotsOnTarget = goals + saves,
            ShotsOffTarget = offTarget,
            ShotsBlocked = blocked,
            WoodworkHits = woodwork,
            Saves = saves,
            Corners = Count(EngineEventType.Corner),
            Offsides = Count(EngineEventType.Offside),
            Fouls = Count(EngineEventType.Foul),
            YellowCards = Count(EngineEventType.YellowCard) + Count(EngineEventType.SecondYellowCard),
            RedCards = Count(EngineEventType.RedCard) + Count(EngineEventType.SecondYellowCard),
            PenaltiesAwarded = Count(EngineEventType.PenaltyAwarded),
            PenaltiesScored = Count(EngineEventType.PenaltyGoal),
            Injuries = Count(EngineEventType.Injury),
            Substitutions = Count(EngineEventType.Substitution),
        };
    }

    private static List<MatchPlayerLineV1> PlayerLines(MatchState state)
    {
        var lines = new List<MatchPlayerLineV1>();

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var runtime = state.SideOf(side);
            var starters = runtime.Lineup.Slots
                .Select(slot => slot.Participant.ParticipantId)
                .ToHashSet();

            foreach (var participant in state.Input.SideOf(side).Squad)
            {
                var id = participant.ParticipantId;
                var started = starters.Contains(id);

                int? entered;

                if (runtime.EnteredMinute.TryGetValue(id, out var cameOn))
                {
                    entered = cameOn;
                }
                else
                {
                    // A starter who was never substituted on has no entry recorded; they were on from kickoff.
                    entered = started ? 0 : null;
                }

                var left = runtime.LeftMinute.TryGetValue(id, out var wentOff)
                    ? wentOff
                    : state.TotalMinutesPlayed;

                lines.Add(new MatchPlayerLineV1
                {
                    ParticipantId = id,
                    ClubId = participant.ClubId,
                    Side = side,
                    Started = started,
                    MinutesPlayed = entered is int fromMinute ? Math.Max(0, left - fromMinute) : 0,
                    Goals = runtime.Goals.TryGetValue(id, out var goals) ? goals : 0,
                    YellowCards = runtime.Yellows.TryGetValue(id, out var yellows) ? yellows : 0,
                    SentOff = runtime.SentOff.Contains(id),
                    AbsenceFixtures = runtime.AbsenceFixtures.TryGetValue(id, out var absence) ? absence : 0,
                });
            }
        }

        // One fixed order, so the canonical output hash does not depend on the order the squads arrived in.
        return [.. lines.OrderBy(line => line.ClubId).ThenBy(line => line.ParticipantId)];
    }
}

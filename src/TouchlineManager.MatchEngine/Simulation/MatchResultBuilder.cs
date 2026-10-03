using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Serialization;
using TouchlineManager.MatchEngine.Spatial;

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
    /// <param name="possessionBallPosition">Where the last possession put the ball, for the events' pitch coordinates.</param>
    /// <remarks>
    /// The builder stamps each stored event with the ball position the possession that produced it had
    /// reached. The event log is replayed in order, so each event's location is the ball's location at
    /// that event, not at the end of the match.
    /// </remarks>
    public static MatchResultV1 Build(MatchState state, string inputHash, SpatialPoint possessionBallPosition)
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
        var saves = SavesByParticipant(state);

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var runtime = state.SideOf(side);
            var starters = runtime.Lineup.Slots
                .Select(slot => slot.Participant.ParticipantId)
                .ToHashSet();

            var goalsFor = state.GoalsOf(side);
            var goalsAgainst = state.GoalsOf(side == MatchSide.Home ? MatchSide.Away : MatchSide.Home);
            var won = goalsFor > goalsAgainst;
            var drew = goalsFor == goalsAgainst;

            // A substitution names the player leaving and the player entering, so the minutes the match
            // center shows are read from the events rather than accumulated in a third place.
            var subbedOut = new Dictionary<Guid, int>();
            var subbedIn = new Dictionary<Guid, int>();

            foreach (var matchEvent in state.Events)
            {
                if (matchEvent.Side != side
                    || matchEvent.Type != EngineEventType.Substitution
                    || matchEvent.ParticipantId is not Guid outgoing
                    || matchEvent.SecondaryParticipantId is not Guid incoming)
                {
                    continue;
                }

                subbedOut[outgoing] = matchEvent.Minute;
                subbedIn[incoming] = matchEvent.Minute;
            }

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

                var minutes = entered is int fromMinute ? Math.Max(0, left - fromMinute) : 0;
                var goals = runtime.Goals.TryGetValue(id, out var scored) ? scored : 0;
                var assists = runtime.Assists.TryGetValue(id, out var setUp) ? setUp : 0;
                var yellows = runtime.Yellows.TryGetValue(id, out var bookings) ? bookings : 0;
                var sentOff = runtime.SentOff.Contains(id);
                var injured = runtime.AbsenceFixtures.ContainsKey(id);

                // A substitute who never came on has no live rating, because a rating is what a player earned
                // on the pitch. An unused bench player's line still exists for the squad's continuity records.
                int? liveRating = runtime.LiveRatings.TryGetValue(id, out var rating)
                    ? rating
                    : null;

                lines.Add(new MatchPlayerLineV1
                {
                    ParticipantId = id,
                    ClubId = participant.ClubId,
                    Side = side,
                    Started = started,
                    MinutesPlayed = minutes,
                    Goals = goals,
                    Assists = assists,
                    PassesAttempted = runtime.PassesAttempted.GetValueOrDefault(id),
                    PassesCompleted = runtime.PassesCompleted.GetValueOrDefault(id),
                    DribblesAttempted = runtime.DribblesAttempted.GetValueOrDefault(id),
                    DribblesCompleted = runtime.DribblesCompleted.GetValueOrDefault(id),
                    YellowCards = yellows,
                    SentOff = sentOff,
                    AbsenceFixtures = runtime.AbsenceFixtures.TryGetValue(id, out var absence) ? absence : 0,
                    RatingBasisPoints = PlayerRatingCalculator.Calculate(
                        state.Rules,
                        new PlayerMatchFacts(
                            minutes,
                            goals,
                            assists,
                            yellows,
                            sentOff,
                            saves.TryGetValue(id, out var made) ? made : 0,
                            won,
                            drew)),
                    FinalConditionBasisPoints = runtime.FinalConditions.TryGetValue(id, out var condition)
                        ? condition
                        : participant.State.ConditionBasisPoints,
                    SubbedOutMinute = subbedOut.TryGetValue(id, out var outMinute) ? outMinute : null,
                    SubbedInMinute = subbedIn.TryGetValue(id, out var inMinute) ? inMinute : null,
                    IsInjured = injured,
                    LiveRatingBasisPoints = liveRating ?? 0,
                });
            }
        }

        // One fixed order, so the canonical output hash does not depend on the order the squads arrived in.
        return [.. lines.OrderBy(line => line.ClubId).ThenBy(line => line.ParticipantId)];
    }

    /// <summary>
    /// Counts each goalkeeper's saves from the event stream.
    /// </summary>
    /// <remarks>
    /// A save is emitted on the shooting side's event, with the keeper who stopped it as the secondary
    /// participant, so the count is keyed on that participant rather than on the event's own side.
    /// </remarks>
    private static Dictionary<Guid, int> SavesByParticipant(MatchState state)
    {
        var saves = new Dictionary<Guid, int>();

        foreach (var matchEvent in state.Events)
        {
            if (matchEvent.Type != EngineEventType.ShotSaved || matchEvent.SecondaryParticipantId is not Guid keeper)
            {
                continue;
            }

            saves.TryGetValue(keeper, out var count);
            saves[keeper] = count + 1;
        }

        return saves;
    }
}

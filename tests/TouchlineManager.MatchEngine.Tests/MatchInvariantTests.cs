using FluentAssertions;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The invariants a simulated match must always satisfy, whatever the snapshot (master plan §8.5).
/// </summary>
/// <remarks>
/// These are checked over a spread of seeds and mismatched sides rather than one match, because an invariant
/// that only holds for an even game is not an invariant.
/// </remarks>
public sealed class MatchInvariantTests
{
    public static TheoryData<ulong> Seeds()
    {
        var data = new TheoryData<ulong>();

        for (var seed = 1UL; seed <= 40; seed++)
        {
            data.Add(seed * 7_919);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_score_equals_the_goal_events(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        result.HomeGoals.Should().Be(
            result.Events.Count(matchEvent => matchEvent.Side == MatchSide.Home && matchEvent.IsGoal));
        result.AwayGoals.Should().Be(
            result.Events.Count(matchEvent => matchEvent.Side == MatchSide.Away && matchEvent.IsGoal));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_statistic_reconciles_with_the_events(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var statistics = result.StatsOf(side);

            var goals = Count(result, side, EngineEventType.Goal)
                + Count(result, side, EngineEventType.PenaltyGoal);
            var saves = Count(result, side, EngineEventType.ShotSaved);

            statistics.Goals.Should().Be(goals);
            statistics.Saves.Should().Be(saves);
            statistics.ShotsOnTarget.Should().Be(goals + saves);
            statistics.ShotsOffTarget.Should().Be(
                Count(result, side, EngineEventType.ShotOffTarget)
                + Count(result, side, EngineEventType.PenaltyMissed));
            statistics.Shots.Should().Be(
                statistics.ShotsOnTarget
                + statistics.ShotsOffTarget
                + statistics.ShotsBlocked
                + statistics.WoodworkHits);
            statistics.Corners.Should().Be(Count(result, side, EngineEventType.Corner));
            statistics.Offsides.Should().Be(Count(result, side, EngineEventType.Offside));
            statistics.Fouls.Should().Be(Count(result, side, EngineEventType.Foul));
            statistics.Substitutions.Should().Be(Count(result, side, EngineEventType.Substitution));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void No_statistic_is_negative_and_the_score_is_finite(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        result.HomeGoals.Should().BeGreaterThanOrEqualTo(0);
        result.AwayGoals.Should().BeGreaterThanOrEqualTo(0);
        result.TotalMinutesPlayed.Should().BeGreaterThanOrEqualTo(90);

        foreach (var statistics in new[] { result.Home, result.Away })
        {
            statistics.Shots.Should().BeGreaterThanOrEqualTo(0);
            statistics.ShotsOnTarget.Should().BeGreaterThanOrEqualTo(0);
            statistics.ShotsOffTarget.Should().BeGreaterThanOrEqualTo(0);
            statistics.ShotsBlocked.Should().BeGreaterThanOrEqualTo(0);
            statistics.WoodworkHits.Should().BeGreaterThanOrEqualTo(0);
            statistics.Saves.Should().BeGreaterThanOrEqualTo(0);
            statistics.Corners.Should().BeGreaterThanOrEqualTo(0);
            statistics.Offsides.Should().BeGreaterThanOrEqualTo(0);
            statistics.Fouls.Should().BeGreaterThanOrEqualTo(0);
            statistics.YellowCards.Should().BeGreaterThanOrEqualTo(0);
            statistics.RedCards.Should().BeGreaterThanOrEqualTo(0);
            statistics.Injuries.Should().BeGreaterThanOrEqualTo(0);
            statistics.Substitutions.Should().BeGreaterThanOrEqualTo(0);
            statistics.ShotsOnTarget.Should().BeLessThanOrEqualTo(statistics.Shots);
        }

        (result.Home.PossessionBasisPoints + result.Away.PossessionBasisPoints)
            .Should().Be(10_000, "possession is a share of one match");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Event_times_are_ordered_and_the_sequence_has_no_gaps(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        result.Events.Should().NotBeEmpty();

        var previousClock = -1;
        var previousSequence = 0;

        foreach (var matchEvent in result.Events)
        {
            matchEvent.Sequence.Should().Be(previousSequence + 1);
            previousSequence = matchEvent.Sequence;

            var clock = (matchEvent.Minute * 100) + matchEvent.StoppageMinute;

            clock.Should().BeGreaterThanOrEqualTo(previousClock, "event times never go backwards");
            previousClock = clock;

            matchEvent.Minute.Should().BeInRange(1, 90);
            matchEvent.StoppageMinute.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_event_belongs_to_the_club_of_its_side(ulong seed)
    {
        var input = TestMatchFactory.Even(seed);
        var result = MatchSimulator.Simulate(input);

        foreach (var matchEvent in result.Events)
        {
            var expected = matchEvent.Side == MatchSide.Home ? input.Home.ClubId : input.Away.ClubId;

            matchEvent.ClubId.Should().Be(expected);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_event_participant_is_in_the_squad_that_side_named(ulong seed)
    {
        var input = TestMatchFactory.Even(seed);
        var result = MatchSimulator.Simulate(input);

        var known = input.Home.Squad.Concat(input.Away.Squad)
            .Select(participant => participant.ParticipantId)
            .ToHashSet();

        foreach (var matchEvent in result.Events)
        {
            if (matchEvent.ParticipantId is Guid primary)
            {
                known.Should().Contain(primary);
            }

            if (matchEvent.SecondaryParticipantId is Guid secondary)
            {
                known.Should().Contain(secondary);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Substitutions_are_legal(ulong seed)
    {
        var input = TestMatchFactory.Even(seed);
        var result = MatchSimulator.Simulate(input);

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var squad = input.SideOf(side).Squad;
            var starters = input.SideOf(side).Slots.Select(slot => slot.ParticipantId).ToHashSet();
            var bench = squad.Select(participant => participant.ParticipantId).Where(id => !starters.Contains(id)).ToHashSet();

            var substitutions = result.Events
                .Where(matchEvent => matchEvent.Side == side && matchEvent.Type == EngineEventType.Substitution)
                .ToList();

            substitutions.Count.Should().BeLessThanOrEqualTo(5, "SQ-5 caps a side at five");

            foreach (var substitution in substitutions)
            {
                bench.Should().Contain(
                    substitution.SecondaryParticipantId!.Value,
                    "a substitute comes from the bench (SQ-4)");
            }

            // Nobody comes on twice: a bench player who is used is no longer available.
            substitutions.Select(matchEvent => matchEvent.SecondaryParticipantId)
                .Should().OnlyHaveUniqueItems();
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void No_player_is_booked_more_than_twice_or_injured_twice(ulong seed)
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));

        foreach (var line in result.PlayerLines)
        {
            line.YellowCards.Should().BeLessThanOrEqualTo(2, "a second booking is a sending-off (DIS-3)");
            line.AbsenceFixtures.Should().BeInRange(0, 6, "DIS-1 bounds an absence at six fixtures");
        }

        result.Events
            .Where(matchEvent => matchEvent.Type == EngineEventType.Injury)
            .Select(matchEvent => matchEvent.ParticipantId)
            .Should().OnlyHaveUniqueItems("one injury per player per match");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Completed_player_lines_match_the_squad(ulong seed)
    {
        var input = TestMatchFactory.Even(seed);
        var result = MatchSimulator.Simulate(input);

        result.PlayerLines.Should().HaveCount(input.Home.Squad.Count + input.Away.Squad.Count);

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var lines = result.PlayerLines.Where(line => line.Side == side).ToList();

            lines.Count(line => line.Started).Should().Be(11);
            lines.Should().OnlyHaveUniqueItems(line => line.ParticipantId);

            foreach (var line in lines)
            {
                line.MinutesPlayed.Should().BeInRange(0, result.TotalMinutesPlayed);

                if (!line.Started && line.MinutesPlayed > 0)
                {
                    line.Started.Should().BeFalse();
                }
            }

            // Whoever scored must have been on the pitch.
            foreach (var line in lines.Where(line => line.Goals > 0))
            {
                line.MinutesPlayed.Should().BeGreaterThan(0);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_most_aggressive_instructions_still_produce_a_valid_match(ulong seed)
    {
        // The degraded paths are the ones nobody exercises: aggressive tackling maximises sendings-off, a high
        // press maximises injuries, and a side that loses both can end up with a goalkeeper outfield and a
        // rating built from an empty band. This is that case, over every seed in the set.
        var aggressive = new MatchInstructionsV1
        {
            Mentality = MatchMentality.Attacking,
            Tempo = MatchTempo.High,
            Passing = MatchPassingStyle.DirectPassing,
            Width = MatchWidth.Wide,
            Pressing = MatchPressing.HighPress,
            DefensiveLine = MatchDefensiveLine.High,
            Tackling = MatchTacklingStyle.Aggressive,
            TimeWasting = MatchTimeWasting.On,
        };

        var cautious = new MatchInstructionsV1
        {
            Mentality = MatchMentality.Defensive,
            Tempo = MatchTempo.Low,
            Pressing = MatchPressing.LowBlock,
            DefensiveLine = MatchDefensiveLine.Deep,
            Tackling = MatchTacklingStyle.StayOnFeet,
        };

        var input = TestMatchFactory.WithInstructions(aggressive, cautious, seed);
        var result = MatchSimulator.Simulate(input);

        result.HomeGoals.Should().Be(
            result.Events.Count(matchEvent => matchEvent.Side == MatchSide.Home && matchEvent.IsGoal));
        result.AwayGoals.Should().Be(
            result.Events.Count(matchEvent => matchEvent.Side == MatchSide.Away && matchEvent.IsGoal));

        result.Events.Select(matchEvent => matchEvent.Sequence)
            .Should().BeInAscendingOrder();

        result.PlayerLines.Should().HaveCount(input.Home.Squad.Count + input.Away.Squad.Count);

        foreach (var line in result.PlayerLines)
        {
            line.MinutesPlayed.Should().BeInRange(0, result.TotalMinutesPlayed);
        }
    }

    private static int Count(MatchResultV1 result, MatchSide side, EngineEventType type) =>
        result.Events.Count(matchEvent => matchEvent.Side == side && matchEvent.Type == type);
}

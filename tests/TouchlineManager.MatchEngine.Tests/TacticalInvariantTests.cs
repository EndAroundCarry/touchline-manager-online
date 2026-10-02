using FluentAssertions;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The tactical invariants of Stage 7: what a sending-off costs, what pressing costs, and what fresh legs
/// are worth.
/// </summary>
/// <remarks>
/// <para>
/// The full measurements live in the simulation laboratory's <c>tactics</c> mode over tens of thousands of
/// fixtures. These are the smaller versions that run in CI, with bands wide enough that a lucky seed cannot
/// fail them and tight enough that a formula change cannot pass them.
/// </para>
/// <para>
/// The condition figures come from the same minute-by-minute capture the match center draws, so the tests
/// and the replay agree about what a player's condition was.
/// </para>
/// </remarks>
public sealed class TacticalInvariantTests
{
    [Fact]
    public void An_early_sending_off_costs_the_side_about_a_goal()
    {
        const int fixtures = 1_800;

        var aggressive = new MatchInstructionsV1 { Tackling = MatchTacklingStyle.Aggressive };

        long difference = 0;
        var dismissed = 0;

        for (var index = 0; index < fixtures; index++)
        {
            // Both sides tackle aggressively, so either can be reduced. Even sides kick off level and the two
            // sides are interchangeable, so the red-carded side's expected goal difference is zero without
            // the card: the mean below is the sending-off's cost, not a home-versus-away artefact.
            var input = TestMatchFactory.Build(
                seed: 30_000UL + (ulong)index,
                homeAbility: 13,
                awayAbility: 13,
                aggressive,
                aggressive);

            var result = MatchSimulator.Simulate(input);

            var homeRed = SentOffBefore(result, MatchSide.Home, minute: 30);
            var awayRed = SentOffBefore(result, MatchSide.Away, minute: 30);

            // Matches where neither side (or both) saw an early red are set aside, so the comparison is never
            // diluted by a second dismissal.
            if (homeRed == awayRed)
            {
                continue;
            }

            difference += homeRed
                ? result.HomeGoals - result.AwayGoals
                : result.AwayGoals - result.HomeGoals;
            dismissed++;
        }

        dismissed.Should().BeGreaterThan(80, "the sample must be large enough for the band to mean something");

        var drop = -(double)difference / dismissed;

        drop.Should().BeInRange(
            0.5,
            2.0,
            "a side sent off before the half hour finishes about a goal and a quarter worse off (Stage 7)");
    }

    [Fact]
    public void A_high_press_side_is_more_fatigued_by_the_eighty_minute()
    {
        const int fixtures = 300;

        var highPress = new MatchInstructionsV1
        {
            Tempo = MatchTempo.High,
            Pressing = MatchPressing.HighPress,
        };
        var lowBlock = new MatchInstructionsV1
        {
            Tempo = MatchTempo.Low,
            Pressing = MatchPressing.LowBlock,
        };

        long pressingSum = 0;
        long pressingCount = 0;
        long sittingSum = 0;
        long sittingCount = 0;

        for (var index = 0; index < fixtures; index++)
        {
            var input = TestMatchFactory.WithInstructions(
                highPress,
                lowBlock,
                seed: 40_000UL + (ulong)index);
            var recorder = new PlayerLiveMetricsRecorder();

            _ = MatchSimulator.Simulate(input, TestMatchFactory.Rules, recorder);

            // Every starter's condition at their last sighting up to the eightieth minute: the low point of
            // the players the side actually used, substitutes included, which is the fatigue the instructions
            // caused rather than the freshness a bench hid.
            var lastAtOrBefore80 = LastConditionsUpTo(recorder, minute: 80);

            Accumulate(input.Home.Slots, lastAtOrBefore80, ref pressingSum, ref pressingCount);
            Accumulate(input.Away.Slots, lastAtOrBefore80, ref sittingSum, ref sittingCount);
        }

        var pressing = (double)pressingSum / pressingCount;
        var sitting = (double)sittingSum / sittingCount;

        (sitting - pressing).Should().BeGreaterThan(
            500,
            "pressing high and playing fast spends condition the low block keeps (Stage 7)");
    }

    [Fact]
    public void A_fresh_substitute_is_sharper_than_the_defenders_who_stayed_on()
    {
        const int fixtures = 300;

        long substituteSum = 0;
        long substituteCount = 0;
        long defenderSum = 0;
        long defenderCount = 0;

        for (var index = 0; index < fixtures; index++)
        {
            var input = TestMatchFactory.Even(seed: 50_000UL + (ulong)index);
            var recorder = new PlayerLiveMetricsRecorder();
            var result = MatchSimulator.Simulate(input, TestMatchFactory.Rules, recorder);

            var byMinute = ConditionsByMinute(recorder);

            foreach (var substitution in result.Events.Where(
                matchEvent => matchEvent.Type == EngineEventType.Substitution && matchEvent.Minute >= 60))
            {
                var incoming = substitution.SecondaryParticipantId!.Value;
                var firstMinute = recorder.Metrics
                    .Where(metric => metric.ParticipantId == incoming)
                    .Select(metric => (int?)metric.Minute)
                    .FirstOrDefault();

                if (firstMinute is null || !byMinute.TryGetValue(firstMinute.Value, out var atMinute))
                {
                    continue;
                }

                var opponent = substitution.Side == MatchSide.Home ? input.Away : input.Home;

                // Only the opponent's starting defenders still on the pitch are "the tired defenders"; a
                // substitute who has just come on is as fresh as the player being compared.
                foreach (var defender in opponent.Slots.Where(slot => slot.Family == MatchPositionFamily.Defence))
                {
                    if (atMinute.TryGetValue(defender.ParticipantId, out var condition))
                    {
                        defenderSum += condition;
                        defenderCount++;
                    }
                }

                substituteSum += atMinute[incoming];
                substituteCount++;
            }
        }

        substituteCount.Should().BeGreaterThan(400, "a few hundred fixtures use their bench many times over");

        var substitute = (double)substituteSum / substituteCount;
        var defenderMean = (double)defenderSum / defenderCount;

        (substitute - defenderMean).Should().BeGreaterThan(
            600,
            "a manager's change brings on legs the tired defenders do not have (Stage 7)");
    }

    private static bool SentOffBefore(MatchResultV1 result, MatchSide side, int minute) =>
        result.Events.Any(matchEvent => matchEvent.Side == side
            && matchEvent.Minute < minute
            && matchEvent.Type is EngineEventType.RedCard or EngineEventType.SecondYellowCard);

    private static Dictionary<Guid, int> LastConditionsUpTo(PlayerLiveMetricsRecorder recorder, int minute)
    {
        var conditions = new Dictionary<Guid, int>();

        foreach (var metric in recorder.Metrics)
        {
            if (metric.Minute <= minute)
            {
                conditions[metric.ParticipantId] = metric.ConditionBasisPoints;
            }
        }

        return conditions;
    }

    private static Dictionary<int, Dictionary<Guid, int>> ConditionsByMinute(PlayerLiveMetricsRecorder recorder)
    {
        var byMinute = new Dictionary<int, Dictionary<Guid, int>>();

        foreach (var metric in recorder.Metrics)
        {
            if (!byMinute.TryGetValue(metric.Minute, out var atMinute))
            {
                atMinute = [];
                byMinute[metric.Minute] = atMinute;
            }

            atMinute[metric.ParticipantId] = metric.ConditionBasisPoints;
        }

        return byMinute;
    }

    private static void Accumulate(
        IReadOnlyList<MatchSlotV1> slots,
        Dictionary<Guid, int> conditions,
        ref long sum,
        ref long count)
    {
        foreach (var slot in slots)
        {
            if (conditions.TryGetValue(slot.ParticipantId, out var condition))
            {
                sum += condition;
                count++;
            }
        }
    }
}

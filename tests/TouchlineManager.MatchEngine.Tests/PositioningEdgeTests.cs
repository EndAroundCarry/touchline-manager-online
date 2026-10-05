using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Positioning helps a player at the finish (`engine-v10`): the one who finds the space is picked to shoot and to
/// head the ball more often, and the edge is bounded so it can never decide a match on its own.
/// </summary>
public sealed class PositioningEdgeTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    [Fact]
    public void The_edge_runs_from_the_floor_to_the_ceiling_and_never_leaves_them()
    {
        PositioningEdge.FromHundredths(MatchAttributeNames.Min * EffectiveSkill.Scale, Rules)
            .Should().Be(Rules.PositioningFloorBasisPoints);
        PositioningEdge.FromHundredths(MatchAttributeNames.Max * EffectiveSkill.Scale, Rules)
            .Should().Be(Rules.PositioningCeilingBasisPoints);

        PositioningEdge.FromHundredths(-500, Rules).Should().Be(Rules.PositioningFloorBasisPoints);
        PositioningEdge.FromHundredths(9_000, Rules).Should().Be(Rules.PositioningCeilingBasisPoints);
    }

    [Fact]
    public void The_edge_only_ever_rises_with_skill()
    {
        var previous = int.MinValue;

        for (var hundredths = 100; hundredths <= 2_000; hundredths += 25)
        {
            var edge = PositioningEdge.FromHundredths(hundredths, Rules);

            edge.Should().BeGreaterThanOrEqualTo(previous);
            previous = edge;
        }
    }

    [Fact]
    public void A_player_in_the_middle_of_the_scale_is_neither_helped_nor_hurt()
    {
        PositioningEdge.FromHundredths(1_050, Rules).Should().Be(EngineRulesV2.Certain);
    }

    [Fact]
    public void A_tired_player_finds_less_space_than_a_fresh_one()
    {
        var fresh = Striker(15, new PlayerCondition(10_000, 0, 10_000, 10_000));
        var tired = fresh with { Condition = new PlayerCondition(0, 0, 10_000, 10_000) };

        PositioningEdge.Of(tired, Rules).Should().BeLessThan(PositioningEdge.Of(fresh, Rules));
    }

    [Fact]
    public void A_defender_reads_the_run_with_both_marking_and_positioning()
    {
        var values = Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray();
        values[(int)MatchAttributeName.Marking] = 20;
        values[(int)MatchAttributeName.Positioning] = 20;

        var sharp = Striker(10, new PlayerCondition(10_000, 0, 10_000, 10_000), PlayerAttributesV1.From(values));

        values[(int)MatchAttributeName.Marking] = 1;

        var onlyPositioned = sharp with { Participant = sharp.Participant with { Attributes = PlayerAttributesV1.From(values) } };

        PositioningEdge.OfDefender(sharp, Rules).Should().BeGreaterThan(PositioningEdge.OfDefender(onlyPositioned, Rules));
        PositioningEdge.OfDefender(onlyPositioned, Rules).Should().BeGreaterThan(Rules.PositioningFloorBasisPoints);
    }

    [Fact]
    public void An_edge_scales_a_weight_and_never_drops_it_to_nothing()
    {
        PositioningEdge.Apply(1_300, EngineRulesV2.Certain).Should().Be(1_300);
        PositioningEdge.Apply(1_300, Rules.PositioningCeilingBasisPoints).Should().BeGreaterThan(1_300);
        PositioningEdge.Apply(1_300, Rules.PositioningFloorBasisPoints).Should().BeInRange(1, 1_299);
    }

    [Fact]
    public void The_striker_who_finds_the_space_takes_more_of_the_shots_and_the_corner_headers()
    {
        const int matches = 300;
        var high = 0;
        var low = 0;

        for (var index = 0; index < matches; index++)
        {
            var input = TestMatchFactory.Even(seed: 7_000UL + (ulong)index);
            var strikers = input.Home.Slots.Where(slot => slot.Family == MatchPositionFamily.Attack).Take(2).ToArray();
            var highId = strikers[0].ParticipantId;
            var lowId = strikers[1].ParticipantId;

            var squad = input.Home.Squad
                .Select(player => player.ParticipantId == highId
                    ? player with { Attributes = WithPositioning(18) }
                    : player.ParticipantId == lowId
                        ? player with { Attributes = WithPositioning(4) }
                        : player)
                .ToList();

            var result = MatchSimulator.Simulate(input with { Home = input.Home with { Squad = squad } });

            foreach (var matchEvent in result.Events.Where(IsShot))
            {
                if (matchEvent.ParticipantId == highId)
                {
                    high++;
                }
                else if (matchEvent.ParticipantId == lowId)
                {
                    low++;
                }
            }
        }

        high.Should().BeGreaterThan((int)(low * 1.2), "the better-placed twin is picked about 1.5 times as often");
    }

    private static bool IsShot(EngineEventV1 matchEvent) =>
        matchEvent.Type is EngineEventType.Goal
            or EngineEventType.ShotSaved
            or EngineEventType.ShotBlocked
            or EngineEventType.ShotOffTarget
            or EngineEventType.Woodwork;

    private static PlayerAttributesV1 WithPositioning(int positioning)
    {
        var values = Enumerable.Repeat(13, MatchAttributeNames.Count).ToArray();
        values[(int)MatchAttributeName.Positioning] = positioning;

        return PlayerAttributesV1.From(values);
    }

    private static ActiveSlot Striker(int positioning, PlayerCondition condition, PlayerAttributesV1? attributes = null)
    {
        var participant = new MatchParticipantV1
        {
            ParticipantId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            ClubId = Guid.NewGuid(),
            DisplayName = "Test Striker",
            ShirtNumber = 9,
            Position = MatchPosition.Striker,
            Attributes = attributes ?? WithPositioning(positioning),
            State = PlayerMatchStateV1.Uniform(10_000),
        };

        return new ActiveSlot
        {
            Slot = new MatchSlotV1
            {
                SlotNumber = 10,
                Family = MatchPositionFamily.Attack,
                Role = MatchRole.Striker,
                X = 5_000,
                Y = 7_200,
                ParticipantId = participant.ParticipantId,
            },
            Participant = participant,
            FamiliarityBasisPoints = EngineRulesV2.Certain,
            Condition = condition,
        };
    }
}

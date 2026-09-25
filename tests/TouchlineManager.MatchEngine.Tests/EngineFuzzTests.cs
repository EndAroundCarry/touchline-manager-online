using FluentAssertions;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The engine either simulates valid input or refuses a malformed one by name (master plan §15.3).
/// </summary>
/// <remarks>
/// A silent wrong answer is the failure mode that matters: a malformed snapshot that produced a plausible
/// result would be undetectable after the fact, because the result would look exactly like a real one. So the
/// property is not "never throws" but "valid input, or a named refusal".
/// </remarks>
public sealed class EngineFuzzTests
{
    public static TheoryData<int> IsolatedMutations() => [.. Enumerable.Range(0, 12)];

    [Fact]
    public void Random_valid_snapshots_always_produce_a_bounded_result()
    {
        var random = new Randomness.Pcg32(99_991UL);

        for (var attempt = 0; attempt < 150; attempt++)
        {
            var input = RandomSnapshot(random);

            var result = MatchSimulator.Simulate(input);

            result.HomeGoals.Should().BeGreaterThanOrEqualTo(0);
            result.AwayGoals.Should().BeGreaterThanOrEqualTo(0);
            result.TotalMinutesPlayed.Should().BeInRange(90, 110);
            result.Events.Should().NotBeEmpty();

            result.Events.Select(matchEvent => matchEvent.Sequence).Should().BeInAscendingOrder();

            result.HomeGoals.Should().Be(
                result.Events.Count(matchEvent => matchEvent.Side == MatchSide.Home && matchEvent.IsGoal));
            result.AwayGoals.Should().Be(
                result.Events.Count(matchEvent => matchEvent.Side == MatchSide.Away && matchEvent.IsGoal));

            (result.Home.PossessionBasisPoints + result.Away.PossessionBasisPoints).Should().Be(10_000);

            result.OutputHash.Should().MatchRegex("^[0-9a-f]{64}$");
        }
    }

    [Fact]
    public void A_lopsided_snapshot_still_produces_a_sane_match()
    {
        var random = new Randomness.Pcg32(7_777UL);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var input = TestMatchFactory.Build(
                seed: random.NextUInt64(),
                homeAbility: random.NextRange(1, 20),
                awayAbility: random.NextRange(1, 20),
                new MatchInstructionsV1());

            var result = MatchSimulator.Simulate(input);

            // However bad one side is, it is still a football match: the ratings floor and the possession bounds
            // exist so that no side is shut out entirely.
            result.Home.Shots.Should().BeGreaterThanOrEqualTo(0);
            result.Away.Shots.Should().BeGreaterThanOrEqualTo(0);

            result.Home.PossessionBasisPoints.Should().BeInRange(2_000, 8_000);
            result.Away.PossessionBasisPoints.Should().BeInRange(2_000, 8_000);
        }
    }

    [Theory]
    [MemberData(nameof(IsolatedMutations))]
    public void A_malformed_snapshot_is_refused_by_name(int mutation)
    {
        var input = TestMatchFactory.Even();

        var mutated = mutation switch
        {
            0 => input with { FixtureId = Guid.Empty },
            1 => input with { WorldId = Guid.Empty },
            2 => input with { SeasonId = Guid.Empty },
            3 => input with { EngineVersion = "engine-v9" },
            4 => input with { RuleSetVersion = "engine-rules-v9" },
            5 => input with { FormulaConfigurationHash = "not-a-hash" },
            6 => input with { Home = input.Home with { Slots = [.. input.Home.Slots.Take(9)] } },
            7 => input with { Home = input.Home with { Slots = [.. input.Home.Slots.TakeLast(9)] } },
            8 => input with
            {
                Home = input.Home with
                {
                    Slots =
                    [
                        input.Home.Slots[0] with { SlotNumber = 0 },
                        .. input.Home.Slots.Skip(1),
                    ],
                },
            },
            9 => input with
            {
                Home = input.Home with
                {
                    Slots =
                    [
                        input.Home.Slots[0] with { X = -1 },
                        .. input.Home.Slots.Skip(1),
                    ],
                },
            },
            10 => input with
            {
                Home = input.Home with { ClubId = Guid.Empty },
            },
            11 => input with
            {
                Home = input.Home with
                {
                    Slots =
                    [
                        input.Home.Slots[0] with { ParticipantId = Guid.Empty },
                        .. input.Home.Slots.Skip(1),
                    ],
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "No such mutation."),
        };

        var act = () => MatchSimulator.Simulate(mutated);

        act.Should().Throw<InvalidMatchInputException>();
    }

    private static MatchInputV1 RandomSnapshot(Randomness.Pcg32 random)
    {
        var homeAbility = random.NextRange(6, 18);
        var awayAbility = random.NextRange(6, 18);

        var home = new MatchInstructionsV1
        {
            Mentality = (MatchMentality)random.NextInt(5),
            Tempo = (MatchTempo)random.NextInt(3),
            Passing = (MatchPassingStyle)random.NextInt(3),
            Width = (MatchWidth)random.NextInt(3),
            Pressing = (MatchPressing)random.NextInt(3),
            DefensiveLine = (MatchDefensiveLine)random.NextInt(3),
            Tackling = (MatchTacklingStyle)random.NextInt(3),
            TimeWasting = (MatchTimeWasting)random.NextInt(3),
        };

        var away = home with { Mentality = (MatchMentality)random.NextInt(5) };

        return TestMatchFactory.Build(random.NextUInt64(), homeAbility, awayAbility, home, away);
    }
}

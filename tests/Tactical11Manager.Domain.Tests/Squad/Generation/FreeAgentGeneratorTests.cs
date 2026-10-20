using FluentAssertions;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;

namespace Tactical11Manager.Domain.Tests.Squad.Generation;

/// <summary>
/// The weekly free-agent batch (`FA-1`…`FA-4`): its size, its split by country and position, its ability band,
/// its hidden gems, and its reproducibility.
/// </summary>
public sealed class FreeAgentGeneratorTests
{
    private const string Seed = "free-agent-world";
    private const int GameYear = 2026;
    private static readonly DateOnly Monday = new(2026, 10, 19);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly FreeAgentCountry England = new("ENG", "england", 1_500);
    private static readonly FreeAgentCountry Spain = new("ESP", "spain", 1_092);

    [Fact]
    public void A_batch_is_a_tenth_of_the_players_under_contract_rounded()
    {
        FreeAgentGenerator.BatchSize(2_592).Should().Be(259, "2,592 contracted players is 259.2");
        FreeAgentGenerator.BatchSize(25).Should().Be(3, "2.5 rounds away from zero");
        FreeAgentGenerator.BatchSize(24).Should().Be(2);
        FreeAgentGenerator.BatchSize(0).Should().Be(0);
        FreeAgentGenerator.BatchSize(100_000).Should().Be(10_000);
    }

    [Fact]
    public void The_family_shares_sum_to_the_whole_batch()
    {
        var shares = new[]
        {
            PositionFamily.Goalkeeper, PositionFamily.Defence, PositionFamily.Midfield, PositionFamily.Attack,
        }.Select(WorldRuleSet.FreeAgentFamilyShareBp).ToList();

        shares.Should().Equal(1_500, 2_800, 2_900, 2_800);
        shares.Sum().Should().Be(10_000);
    }

    [Theory]
    [InlineData(259, 1_500, 1_092)]
    [InlineData(259, 864, 864)]
    [InlineData(1, 10, 10)]
    [InlineData(7, 1, 1_000)]
    [InlineData(0, 5, 5)]
    [InlineData(100, 0, 1_000)]
    public void The_split_sums_exactly_for_the_batch_and_for_every_country(int size, int english, int spanish)
    {
        var plans = FreeAgentGenerator.Plan(
            size,
            [England with { ContractedPlayers = english }, Spain with { ContractedPlayers = spanish }]);

        plans.Sum(plan => plan.Positions.Count).Should().Be(size);

        foreach (var plan in plans)
        {
            var expectedShare = size * (double)plan.Country.ContractedPlayers / (english + spanish);

            plan.Positions.Count.Should().BeInRange(
                (int)Math.Floor(expectedShare),
                (int)Math.Ceiling(expectedShare),
                "a country's share follows its contracted players to within one");

            var families = plan.Positions.GroupBy(PlayerPositions.FamilyOf).ToDictionary(g => g.Key, g => g.Count());

            foreach (var family in Enum.GetValues<PositionFamily>())
            {
                var exact = plan.Positions.Count * WorldRuleSet.FreeAgentFamilyShareBp(family) / 10_000d;

                families.GetValueOrDefault(family).Should().BeInRange(
                    (int)Math.Floor(exact),
                    (int)Math.Ceiling(exact),
                    $"{family} follows its share to within one player");
            }
        }
    }

    [Fact]
    public void A_batch_of_the_size_of_a_fresh_world_follows_the_position_shares_and_has_every_position()
    {
        var batch = FreeAgentGenerator.Generate(Request(England, Spain));

        batch.Should().HaveCount(259);

        var families = batch.GroupBy(agent => PlayerPositions.FamilyOf(agent.Player.PrimaryPosition))
            .ToDictionary(group => group.Key, group => group.Count());

        families[PositionFamily.Goalkeeper].Should().BeInRange(38, 40, "15% of 259 is 38.9");
        families[PositionFamily.Defence].Should().BeInRange(72, 73, "28% is 72.5");
        families[PositionFamily.Midfield].Should().BeInRange(75, 76, "29% is 75.1");
        families[PositionFamily.Attack].Should().BeInRange(72, 73, "28% is 72.5");

        batch.Select(agent => agent.Player.PrimaryPosition).Distinct()
            .Should().BeEquivalentTo(PlayerPositions.All, "full-backs and wingers are listed as well as centre-backs");
    }

    [Fact]
    public void The_same_seed_and_week_reproduce_the_same_batch_and_another_week_does_not()
    {
        Rows(FreeAgentGenerator.Generate(Request(England, Spain)))
            .Should().Equal(Rows(FreeAgentGenerator.Generate(Request(England, Spain))));

        Rows(FreeAgentGenerator.Generate(Request(England, Spain)))
            .Should().NotEqual(Rows(FreeAgentGenerator.Generate(Request(England, Spain) with { WeekStart = Monday.AddDays(7) })));

        Rows(FreeAgentGenerator.Generate(Request(England, Spain)))
            .Should().NotEqual(Rows(FreeAgentGenerator.Generate(Request(England, Spain) with { WorldSeed = "another-world" })));
    }

    [Fact]
    public void Every_free_agent_shows_an_ability_of_six_to_ten_and_has_no_club()
    {
        var batch = FreeAgentGenerator.Generate(Request(new FreeAgentCountry("ENG", "england", 100_000)));

        batch.Should().HaveCount(10_000);

        foreach (var agent in batch)
        {
            var attributes = agent.Attributes.ToSet();
            var shown = (int)Math.Round(attributes.Values.Average(), MidpointRounding.AwayFromZero);

            shown.Should().BeInRange(WorldRuleSet.FreeAgentAbilityMin, WorldRuleSet.FreeAgentAbilityMax);
            shown.Should().Be(agent.Ability);
            attributes.IsWithinScale.Should().BeTrue();
            agent.Player.Status.Should().Be(PlayerStatus.FreeAgent);
            agent.Player.NationalityCode.Should().Be("ENG");
            agent.State.PlayerId.Should().Be(agent.Player.Id);
            agent.Attributes.PlayerId.Should().Be(agent.Player.Id);
        }

        var byAbility = batch.GroupBy(agent => agent.Ability).ToDictionary(group => group.Key, group => group.Count());

        byAbility.Keys.Should().BeEquivalentTo([6, 7, 8, 9, 10]);
        byAbility.OrderByDescending(pair => pair.Value).First().Key.Should().Be(8, "eight is the commonest");
    }

    [Fact]
    public void One_in_a_hundred_is_a_young_gem_who_looks_like_the_rest()
    {
        var batch = FreeAgentGenerator.Generate(Request(new FreeAgentCountry("ENG", "england", 100_000)));
        var gems = batch.Where(agent => agent.IsGem).ToList();

        gems.Count.Should().BeInRange(70, 130, "1% of 10,000 is 100, within 0.3 percentage points");

        foreach (var gem in gems)
        {
            gem.Player.AgeIn(GameYear).Should().BeInRange(
                WorldRuleSet.PlayerMinimumAge,
                WorldRuleSet.FreeAgentGemAgeMax);
            gem.Player.Potential.Should().BeInRange(
                WorldRuleSet.FreeAgentGemPotentialMin,
                WorldRuleSet.FreeAgentGemPotentialMax);
            gem.Ability.Should().BeInRange(WorldRuleSet.FreeAgentAbilityMin, WorldRuleSet.FreeAgentAbilityMax);
        }

        gems.Select(gem => gem.Player.Potential).Distinct().Should().HaveCount(4, "13, 14, 15 and 16 all turn up");
        gems.Select(gem => gem.Ability).Distinct().Should().HaveCountGreaterThan(2, "a gem shows the same range as anyone");

        foreach (var normal in batch.Where(agent => !agent.IsGem))
        {
            normal.Player.AgeIn(GameYear).Should().BeInRange(WorldRuleSet.FreeAgentAgeMin, WorldRuleSet.FreeAgentAgeMax);
            normal.Player.Potential.Should().BeLessThanOrEqualTo(WorldRuleSet.FreeAgentPotentialCap);
            normal.Player.Potential.Should().BeGreaterThanOrEqualTo(normal.Ability);

            if (normal.Player.AgeIn(GameYear) > WorldRuleSet.FreeAgentHeadroomAgeMax)
            {
                normal.Player.Potential.Should().Be(normal.Ability, "no headroom past 23");
            }
            else
            {
                normal.Player.Potential.Should().BeLessThanOrEqualTo(normal.Ability + WorldRuleSet.FreeAgentHeadroomMax);
            }
        }
    }

    [Fact]
    public void A_free_agent_starts_with_the_experience_his_age_gives_him()
    {
        foreach (var agent in FreeAgentGenerator.Generate(Request(England, Spain)))
        {
            agent.State.ExperienceBp.Should().Be(PlayerExperience.StartingFor(agent.Player.AgeIn(GameYear)));
        }
    }

    private static FreeAgentBatchRequest Request(params FreeAgentCountry[] countries) =>
        new(Seed, Guid.CreateVersion7(), Monday, GameYear, countries, Now);

    private static IReadOnlyList<string> Rows(IReadOnlyList<GeneratedFreeAgent> batch) =>
    [
        .. batch.Select(agent => string.Join(
            '|',
            agent.Player.FullName,
            agent.Player.PrimaryPosition.ToCode(),
            agent.Player.BirthGameYear,
            agent.Player.BirthDayOfYear,
            agent.Player.Potential,
            agent.Player.Reputation,
            agent.IsGem,
            string.Join(',', agent.Attributes.ToSet().Values))),
    ];
}

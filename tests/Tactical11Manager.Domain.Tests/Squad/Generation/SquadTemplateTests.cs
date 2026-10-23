using System.Globalization;
using FluentAssertions;
using FluentAssertions.Execution;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Tests.Squad.Generation;

/// <summary>
/// Fair seeded squads (plan cheeky-dongarra): every club of a tier opens with the same mix of abilities, a little diversity, and the same number of
/// talents and gems.
/// </summary>
public sealed class SquadTemplateTests
{
    private const string Seed = "fair-squads";
    private const int ClubsPerTier = 16;
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tier_one_is_three_twelves_four_elevens_five_tens_six_nines_and_six_eights()
    {
        Counts(SquadTemplate.BaseRatingsFor(1)).Should().Equal(
            new Dictionary<int, int> { [12] = 3, [11] = 4, [10] = 5, [9] = 6, [8] = 6 });
        SquadTemplate.BaseRatingsFor(1).Average().Should().BeApproximately(9.67, 0.01);
    }

    [Fact]
    public void Tier_two_is_three_tens_nine_nines_and_twelve_eights()
    {
        Counts(SquadTemplate.BaseRatingsFor(2)).Should().Equal(
            new Dictionary<int, int> { [10] = 3, [9] = 9, [8] = 12 });
        SquadTemplate.BaseRatingsFor(2).Average().Should().BeApproximately(8.63, 0.01);
    }

    [Fact]
    public void A_template_has_a_rating_for_every_slot_of_the_composition()
    {
        foreach (var tier in new[] { 1, 2, 3, 6 })
        {
            SquadTemplate.BaseRatingsFor(tier).Should().HaveCount(WorldRuleSet.GeneratorSquadTarget, $"tier {tier}");
        }
    }

    [Fact]
    public void A_deeper_tier_is_tier_twos_mix_shifted_down_one_rating_per_tier_and_never_below_five()
    {
        SquadTemplate.BandFor(3).Should().Be((7, 9));
        SquadTemplate.BandFor(4).Should().Be((6, 8));
        SquadTemplate.BaseRatingsFor(3).Should().Equal(SquadTemplate.BaseRatingsFor(2).Select(rating => rating - 1));
        SquadTemplate.BaseRatingsFor(4).Should().Equal(SquadTemplate.BaseRatingsFor(2).Select(rating => rating - 2));

        foreach (var tier in Enumerable.Range(1, 12))
        {
            SquadTemplate.BaseRatingsFor(tier).Should().OnlyContain(rating => rating >= SquadTemplate.RatingFloor, $"tier {tier}");

            var (low, high) = SquadTemplate.BandFor(tier);

            SquadTemplate.BaseRatingsFor(tier).Should().OnlyContain(rating => rating >= low && rating <= high, $"tier {tier}");
        }
    }

    [Fact]
    public void Every_player_shows_exactly_his_slots_rating_nudges_included()
    {
        using var scope = new AssertionScope();

        foreach (var tier in new[] { 1, 2, 3 })
        {
            for (var ordinal = 0; ordinal < 40; ordinal++)
            {
                var plan = SquadTemplate.PlanFor(Seed, "ENG", ordinal, tier);
                var squad = Generate("ENG", "england", ordinal, tier);

                for (var slot = 0; slot < plan.Count; slot++)
                {
                    squad[slot].Player.PrimaryPosition.Should().Be(plan[slot].Position);
                    Ability(squad[slot]).Should().Be(plan[slot].Rating, $"tier {tier} club {ordinal} slot {slot}");
                    squad[slot].Player.AgeIn(2026).Should().Be(plan[slot].Age);
                    squad[slot].Player.Potential.Should().Be(plan[slot].Potential);
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Nudges_stay_inside_the_tiers_band_and_a_club_stays_within_four_of_the_template(int tier)
    {
        var (low, high) = SquadTemplate.BandFor(tier);
        var template = SquadTemplate.BaseRatingsFor(tier).Sum();

        for (var ordinal = 0; ordinal < 200; ordinal++)
        {
            var ratings = SquadTemplate.PlanFor(Seed, "ENG", ordinal, tier).Select(slot => slot.Rating).ToList();

            ratings.Should().OnlyContain(rating => rating >= low && rating <= high, $"club {ordinal}");
            Math.Abs(ratings.Sum() - template).Should().BeLessThanOrEqualTo(SquadTemplate.NudgeCount, $"club {ordinal}");
            ratings.Sum().Should().Be(template, $"club {ordinal}: half the nudges raise a slot and half lower one");
        }
    }

    [Fact]
    public void Clubs_differ_a_little_and_the_diversity_is_seeded()
    {
        var plans = Enumerable.Range(0, ClubsPerTier)
            .Select(ordinal => string.Join(',', SquadTemplate.PlanFor(Seed, "ENG", ordinal, 1).Select(slot => slot.Rating)))
            .ToList();

        plans.Distinct().Count().Should().BeGreaterThan(ClubsPerTier / 2, "mirroring and nudges give each club its own mix");
        string.Join(',', SquadTemplate.PlanFor(Seed, "ENG", 3, 1).Select(slot => slot.Rating))
            .Should().Be(string.Join(',', SquadTemplate.PlanFor(Seed, "ENG", 3, 1).Select(slot => slot.Rating)));
        SquadTemplate.PlanFor(Seed, "ENG", 3, 1).Select(slot => slot.Potential)
            .Should().NotEqual(SquadTemplate.PlanFor("another-seed", "ENG", 3, 1).Select(slot => slot.Potential));
    }

    [Fact]
    public void Mirroring_swaps_the_two_flanks_and_nothing_else()
    {
        var mirrored = 0;

        for (var ordinal = 0; ordinal < 100; ordinal++)
        {
            var plan = SquadTemplate.PlanFor(Seed, "ENG", ordinal, 1);
            var leftBacks = plan.Where(slot => slot.Position == PlayerPosition.LeftBack).Sum(slot => slot.Rating);
            var rightBacks = plan.Where(slot => slot.Position == PlayerPosition.RightBack).Sum(slot => slot.Rating);

            // The template's left backs are rated 10 and 8 and its right backs 11 and 8; a nudge moves a rating by one.
            if (rightBacks < leftBacks)
            {
                mirrored++;
            }
        }

        mirrored.Should().BeInRange(15, 85, "a coin decides which side is the stronger one");
    }

    [Fact]
    public void Every_squad_has_exactly_one_gem_and_three_talents_who_climb_above_their_age()
    {
        using var scope = new AssertionScope();

        foreach (var tier in new[] { 1, 2, 3 })
        {
            for (var ordinal = 0; ordinal < 200; ordinal++)
            {
                var plan = SquadTemplate.PlanFor(Seed, "ENG", ordinal, tier);

                plan.Count(slot => slot.Kind == PotentialKind.Gem).Should().Be(SquadTemplate.GemsPerSquad, $"tier {tier} club {ordinal}");
                plan.Count(slot => slot.Kind == PotentialKind.Talent).Should().Be(SquadTemplate.TalentsPerSquad, $"tier {tier} club {ordinal}");

                foreach (var slot in plan)
                {
                    var normal = SquadTemplate.NormalPotential(slot.Rating, slot.Age);

                    slot.Potential.Should().BeInRange(slot.Rating, WorldRuleSet.AttributeMax);

                    switch (slot.Kind)
                    {
                        case PotentialKind.Normal:
                            slot.Potential.Should().Be(normal);
                            break;
                        case PotentialKind.Talent:
                            slot.Potential.Should().BeGreaterThanOrEqualTo(Math.Min(normal + 1, WorldRuleSet.AttributeMax));
                            slot.Potential.Should().BeGreaterThanOrEqualTo(Math.Min(slot.Rating + 3, WorldRuleSet.AttributeMax));
                            break;
                        case PotentialKind.Gem:
                            slot.Potential.Should().BeGreaterThanOrEqualTo(Math.Min(normal + 2, WorldRuleSet.AttributeMax));
                            slot.Potential.Should().BeGreaterThanOrEqualTo(Math.Min(slot.Rating + 6, WorldRuleSet.AttributeMax));
                            break;
                    }
                }
            }
        }
    }

    [Fact]
    public void Talents_and_the_gem_are_young_players_when_the_squad_has_enough_of_them()
    {
        for (var ordinal = 0; ordinal < 200; ordinal++)
        {
            var plan = SquadTemplate.PlanFor(Seed, "ENG", ordinal, 1);
            var young = plan.Count(slot => slot.Age <= SquadTemplate.YoungestPickAge);
            var picked = plan.Where(slot => slot.Kind != PotentialKind.Normal).ToList();

            if (young >= SquadTemplate.GemsPerSquad + SquadTemplate.TalentsPerSquad)
            {
                picked.Should().OnlyContain(slot => slot.Age <= SquadTemplate.YoungestPickAge, $"club {ordinal}");
            }
            else
            {
                var oldestPicked = picked.Max(slot => slot.Age);

                plan.Count(slot => slot.Age < oldestPicked).Should().BeLessThan(
                    SquadTemplate.GemsPerSquad + SquadTemplate.TalentsPerSquad,
                    $"club {ordinal}: the youngest are picked");
            }
        }
    }

    [Fact]
    public void A_player_who_is_neither_talent_nor_gem_keeps_room_to_grow_only_while_young()
    {
        SquadTemplate.NormalPotential(10, 17).Should().Be(14);
        SquadTemplate.NormalPotential(10, 20).Should().Be(12);
        SquadTemplate.NormalPotential(10, 23).Should().Be(11);
        SquadTemplate.NormalPotential(10, 24).Should().Be(10);
        SquadTemplate.NormalPotential(10, 31).Should().Be(10);
        SquadTemplate.NormalPotential(19, 17).Should().Be(WorldRuleSet.AttributeMax, "potential is capped");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_best_eleven_of_one_tier_is_within_seven_percent_across_every_country(int tier)
    {
        var strengths = new List<(string Club, int Strength)>();

        foreach (var country in LaunchCountries.All)
        {
            for (var ordinal = 0; ordinal < ClubsPerTier; ordinal++)
            {
                var squad = Generate(country.Code, country.NamePoolKey, ordinal, tier);

                strengths.Add(($"{country.Code} {ordinal}", StrengthOf(squad)));
            }
        }

        var weakest = strengths.MinBy(entry => entry.Strength);
        var strongest = strengths.MaxBy(entry => entry.Strength);
        var ratio = (double)strongest.Strength / weakest.Strength;

        ratio.Should().BeLessThanOrEqualTo(
            1.07,
            $"tier {tier}: {strongest.Club} at {strongest.Strength} against {weakest.Club} at {weakest.Strength}");
    }

    [Fact]
    public void A_template_squad_is_legal_and_reproducible()
    {
        var first = Generate("ENG", "england", 4, 1);
        var second = Generate("ENG", "england", 4, 1);

        first.Should().HaveCount(WorldRuleSet.GeneratorSquadTarget);
        SquadLegality.IsLegal(first.Count, first.Select(member => PlayerPositions.FamilyOf(member.Player.PrimaryPosition)).ToList())
            .Should().BeTrue();
        first.Select(member => member.Player.FullName).Should().OnlyHaveUniqueItems();
        first.Select(member => member.Attributes.ToSet().Values.ToArray())
            .Should().BeEquivalentTo(second.Select(member => member.Attributes.ToSet().Values.ToArray()), options => options.WithStrictOrdering());
        first.Select(member => member.Contract.SeasonWageMinor).Should().Equal(second.Select(member => member.Contract.SeasonWageMinor));
        first.Should().OnlyContain(member => member.Attributes.ToSet().IsWithinScale);
    }

    [Fact]
    public void Wages_follow_the_ratings_so_two_clubs_of_a_tier_pay_much_the_same()
    {
        var bills = Enumerable.Range(0, ClubsPerTier)
            .Select(ordinal => Generate("ENG", "england", ordinal, 1).Sum(member => member.Contract.SeasonWageMinor))
            .ToList();

        ((double)bills.Max() / bills.Min()).Should().BeLessThan(1.15);
    }

    [Fact]
    public void A_template_squad_matches_its_pinned_golden_values()
    {
        var squad = Generate("ENG", "england", 0, 1, seed: "fair-golden");

        GoldenDigest(squad).Should().Be(
            "46f9a3f906c6e96c",
            "player-gen-v4 and squad-template-v1 fix every slot's rating (plan cheeky-dongarra); every name pool is 36 by 37 from player-name-pools-v2 (the digest before was 89116c61d922b1b8)");
    }

    [Theory]
    [InlineData(1, 8, 9)]
    [InlineData(2, 8, 8)]
    [InlineData(3, 7, 7)]
    public void An_emergency_replacement_is_one_of_the_tiers_lowest_ratings(int tier, int lowest, int highest)
    {
        var seen = new HashSet<int>();

        for (var ordinal = 0; ordinal < 60; ordinal++)
        {
            var member = PlayerGenerator.GenerateEmergencyReplacement(
                Seed,
                "england",
                "ENG",
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                tier,
                Guid.CreateVersion7(),
                seasonNumber: 1,
                gameYear: 2026,
                PlayerPosition.CentreBack,
                ordinal,
                Now);

            Ability(member).Should().BeInRange(lowest, highest);
            member.Player.Potential.Should().BeGreaterThanOrEqualTo(Ability(member));
            seen.Add(Ability(member));
        }

        seen.Should().BeEquivalentTo(Enumerable.Range(lowest, highest - lowest + 1));
    }

    [Fact]
    public void The_template_is_part_of_the_versions_a_run_records()
    {
        SquadTemplate.Version.Should().NotBeNullOrWhiteSpace();
        PlayerGenerator.Version.Should().Be("player-gen-v4");
        WorldRuleSet.Version.Should().Be("world-rules-v19");
    }

    private static int StrengthOf(IReadOnlyList<GeneratedSquadMember> squad) =>
        TeamStrength.OfSquad(
        [
            .. squad.Select(member => new AiSquadPlayer(
                member.Player.Id,
                member.Player.PrimaryPosition,
                member.Player.SecondaryPositions,
                member.Attributes.ToSet().Values,
                member.State.ConditionBp,
                IsAvailable: true)),
        ]);

    private static int Ability(GeneratedSquadMember member) =>
        (int)Math.Round(member.Attributes.ToSet().Values.Average(), MidpointRounding.AwayFromZero);

    private static Dictionary<int, int> Counts(IEnumerable<int> ratings) =>
        ratings.GroupBy(rating => rating).ToDictionary(group => group.Key, group => group.Count());

    private static IReadOnlyList<GeneratedSquadMember> Generate(
        string countryCode,
        string poolKey,
        int clubOrdinal,
        int tier,
        string seed = Seed) =>
        PlayerGenerator.GenerateTemplateSquad(new SquadGenerationRequest(
            seed,
            poolKey,
            countryCode,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            clubOrdinal,
            tier,
            Guid.CreateVersion7(),
            SeasonNumber: 1,
            GameYear: 2026,
            Now));

    private static string GoldenDigest(IReadOnlyList<GeneratedSquadMember> squad) =>
        DeterministicDigest.Of(
        [
            .. squad.SelectMany(member => new[]
            {
                member.Player.FullName,
                member.Player.PrimaryPosition.ToCode(),
                member.Player.SecondaryPositionCodes,
                member.Player.BirthGameYear.ToString(CultureInfo.InvariantCulture),
                member.Player.Potential.ToString(CultureInfo.InvariantCulture),
                string.Join('|', member.Attributes.ToSet().Values),
                member.Contract.SeasonWageMinor.ToString(CultureInfo.InvariantCulture),
                member.Contract.SquadStatus.ToCode(),
            }),
        ])[..16];
}

using System.Globalization;
using FluentAssertions;
using FluentAssertions.Execution;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.Squad.Generation;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Tests.Squad.Generation;

/// <summary>
/// Generated squads (`SQ-1`, `SQ-2`, `SQ-6`, `CON-1`, `TRN-4`, `TRN-5..TRN-7`, `FIC-7`, `PYR-14`).
/// These are the tests that make "the same seed reproduces the same world" mean something for players.
/// </summary>
public sealed class PlayerGeneratorTests
{
    private const string Seed = "stage-4-world";
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_same_seed_and_club_ordinal_reproduce_the_same_squad()
    {
        Rows(Generate()).Should().Equal(
            Rows(Generate()),
            "the same seed and version must reproduce the same squad (FIC-7)");
    }

    [Fact]
    public void A_different_seed_produces_a_different_squad()
    {
        Rows(Generate(seed: "stage-4-world")).Should().NotEqual(Rows(Generate(seed: "another-world")));
    }

    [Fact]
    public void Two_clubs_in_the_same_country_get_different_squads()
    {
        Rows(Generate(clubOrdinal: 0)).Should().NotEqual(Rows(Generate(clubOrdinal: 1)));
    }

    [Fact]
    public void One_ordinal_under_two_countries_pools_produces_different_players()
    {
        var england = Generate(countryCode: "ENG", poolKey: "england");
        var romania = Generate(countryCode: "ROU", poolKey: "romania");

        england.Select(member => member.Player.FullName)
            .Should().NotIntersectWith(romania.Select(member => member.Player.FullName));
    }

    [Fact]
    public void A_generated_squad_is_legal()
    {
        var squad = Generate();

        squad.Should().HaveCount(WorldRuleSet.GeneratorSquadTarget, "SQ-1");

        var families = squad.Select(member => PlayerPositions.FamilyOf(member.Player.PrimaryPosition)).ToList();

        SquadLegality.IsLegal(squad.Count, families).Should().BeTrue(
            "a generated squad is within bounds and carries at least two goalkeepers (SQ-2)");

        squad.Should().OnlyContain(member => member.Contract.IsActive, "SQ-6");
        squad.Should().OnlyContain(member => member.Registration.IsActive, "SQ-6");
        squad.Select(member => member.Contract.PlayerId)
            .Should().OnlyHaveUniqueItems("a player has exactly one active contract (SQ-6)");
        squad.Select(member => member.Registration.PlayerId)
            .Should().OnlyHaveUniqueItems("a player has exactly one active registration (SQ-6)");
    }

    [Fact]
    public void The_generated_composition_matches_the_rule_set()
    {
        var families = Generate()
            .Select(member => PlayerPositions.FamilyOf(member.Player.PrimaryPosition))
            .ToList();

        families.Count(family => family == PositionFamily.Goalkeeper)
            .Should().Be(WorldRuleSet.GeneratedGoalkeepers, "SQ-1");
        families.Count(family => family == PositionFamily.Defence)
            .Should().Be(WorldRuleSet.GeneratedDefenders, "SQ-1");
        families.Count(family => family == PositionFamily.Midfield)
            .Should().Be(WorldRuleSet.GeneratedMidfielders, "SQ-1");
        families.Count(family => family == PositionFamily.Attack)
            .Should().Be(WorldRuleSet.GeneratedAttackers, "SQ-1");
    }

    [Fact]
    public void Every_generated_attribute_is_on_the_displayed_scale()
    {
        var attributes = Generate().Select(member => member.Attributes.ToSet());

        attributes.Should().OnlyContain(set => set.IsWithinScale, "TRN-4");
        attributes.Should().OnlyContain(set => set.Values.Count == AttributeNames.Count);
    }

    [Fact]
    public void Every_generated_state_value_is_within_its_basis_point_scale()
    {
        foreach (var member in Generate())
        {
            member.State.ConditionBp.Should().BeInRange(
                WorldRuleSet.StateBasisPointsMin,
                WorldRuleSet.StateBasisPointsMax,
                "TRN-5");
            member.State.FatigueBp.Should().BeInRange(
                WorldRuleSet.StateBasisPointsMin,
                WorldRuleSet.StateBasisPointsMax,
                "TRN-6");
            member.State.MoraleBp.Should().BeInRange(
                WorldRuleSet.StateBasisPointsMin,
                WorldRuleSet.StateBasisPointsMax,
                "TRN-7");
            member.State.MatchSharpnessBp.Should().BeInRange(
                WorldRuleSet.StateBasisPointsMin,
                WorldRuleSet.StateBasisPointsMax,
                "TRN-7");
            member.State.DevelopmentRemainder.Should().Be(0, "TRN-10 seeds the carry-forward at zero");
        }
    }

    [Fact]
    public void Generated_ages_and_contract_terms_are_inside_their_rule_set_bounds()
    {
        const int gameYear = 2026;
        const int seasonNumber = 1;

        foreach (var member in Generate(seasonNumber: seasonNumber, gameYear: gameYear))
        {
            var age = member.Player.AgeIn(gameYear);

            age.Should().BeInRange(WorldRuleSet.PlayerMinimumAge, WorldRuleSet.PlayerMaximumAge);
            member.Player.BirthDayOfYear.Should().BeInRange(1, 366);

            member.Contract.StartSeasonNumber.Should().Be(seasonNumber, "CON-8: terms are season numbers");
            (member.Contract.EndSeasonNumber - member.Contract.StartSeasonNumber + 1)
                .Should().BeInRange(WorldRuleSet.ContractMinSeasons, WorldRuleSet.ContractMaxSeasons, "CON-1");
            member.Contract.WeeklyWageMinor.Should().BePositive();
        }
    }

    [Fact]
    public void Every_player_in_a_squad_has_a_distinct_full_name()
    {
        // The pool sizes are coprime and larger than a squad, so this holds by construction rather than by
        // a retry loop (FIC-4).
        Generate().Select(member => member.Player.FullName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_generated_name_clears_the_fictional_identity_blocklist()
    {
        foreach (var poolKey in PlayerNamePools.Keys)
        {
            var squad = Generate(
                seed: $"blocklist-{poolKey}",
                countryCode: poolKey[..3].ToUpperInvariant(),
                poolKey: poolKey);

            squad.Should().OnlyContain(member =>
                !FictionalIdentityBlocklist.ContainsBlockedIdentity(member.Player.FullName),
                "FIC-5: a generated name is never a real football identity");
        }
    }

    [Fact]
    public void A_deeper_tier_generates_a_weaker_squad()
    {
        MeanAbility(Generate(tier: 1)).Should()
            .BeGreaterThan(MeanAbility(Generate(tier: 3)), "ability falls with depth in the pyramid");
    }

    [Fact]
    public void The_generator_versions_are_named_for_the_run_to_record()
    {
        PlayerGenerator.Version.Should().NotBeNullOrWhiteSpace("FIC-8");
        PlayerAttributeProfiles.Version.Should().NotBeNullOrWhiteSpace("FIC-8");
        PlayerNamePools.Version.Should().NotBeNullOrWhiteSpace("FIC-8");
    }

    /// <summary>
    /// Pins one club's squad exactly, so an unintended change to a pool, a profile, or the draw order fails
    /// here rather than silently rewriting every world. A deliberate change is made by re-pinning, which is
    /// also the moment the generator version is bumped (`FIC-8`).
    /// </summary>
    [Fact]
    public void The_generated_squad_matches_its_pinned_golden_values()
    {
        var squad = Generate(seed: "stage-4-golden");

        using var scope = new AssertionScope();

        squad.Take(3).Select(member => member.Player.FullName)
            .Should().Equal("Alaric Alderwick", "Bramwell Brambleby", "Corin Cawthorne");

        GoldenDigest(squad).Should().Be("9bcc43b9c216e880");
    }

    private static string GoldenDigest(IReadOnlyList<GeneratedSquadMember> squad) =>
        DeterministicDigest.Of(
        [
            .. squad.SelectMany(member => new[]
            {
                member.Player.FullName,
                member.Player.ShortName,
                member.Player.PrimaryPosition.ToCode(),
                member.Player.SecondaryPositionCodes,
                member.Player.BirthGameYear.ToString(CultureInfo.InvariantCulture),
                member.Player.BirthDayOfYear.ToString(CultureInfo.InvariantCulture),
                member.Player.PreferredFoot.ToCode(),
                member.Player.HeightCm.ToString(CultureInfo.InvariantCulture),
                member.Player.WeightKg.ToString(CultureInfo.InvariantCulture),
                member.Player.Potential.ToString(CultureInfo.InvariantCulture),
                member.Player.Reputation.ToString(CultureInfo.InvariantCulture),
                string.Join('|', member.Attributes.ToSet().Values),
                member.Contract.WeeklyWageMinor.ToString(CultureInfo.InvariantCulture),
                member.Contract.SquadStatus.ToCode(),
                member.Contract.EndSeasonNumber.ToString(CultureInfo.InvariantCulture),
            }),
        ])[..16];

    private static double MeanAbility(IReadOnlyList<GeneratedSquadMember> squad) =>
        squad.Average(member => member.Attributes.ToSet().Values.Average());

    private static IReadOnlyList<SquadRow> Rows(IReadOnlyList<GeneratedSquadMember> squad) =>
    [
        .. squad.Select(member => new SquadRow(
            member.Player.FullName,
            member.Player.ShortName,
            member.Player.BirthGameYear,
            member.Player.BirthDayOfYear,
            member.Player.PreferredFoot,
            member.Player.HeightCm,
            member.Player.WeightKg,
            member.Player.PrimaryPosition,
            member.Player.SecondaryPositionCodes,
            member.Player.Potential,
            member.Player.Reputation,
            member.Attributes.ToSet(),
            member.Contract.WeeklyWageMinor,
            member.Contract.SquadStatus,
            member.Contract.StartSeasonNumber,
            member.Contract.EndSeasonNumber,
            member.State.ConditionBp,
            member.State.MoraleBp)),
    ];

    private static IReadOnlyList<GeneratedSquadMember> Generate(
        string seed = Seed,
        string countryCode = "ENG",
        string poolKey = "england",
        int clubOrdinal = 0,
        int tier = 1,
        int seasonNumber = 1,
        int gameYear = 2026) =>
        PlayerGenerator.GenerateSquad(new SquadGenerationRequest(
            seed,
            poolKey,
            countryCode,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            clubOrdinal,
            tier,
            Guid.CreateVersion7(),
            seasonNumber,
            gameYear,
            Now));

    /// <summary>The part of a generated player that must be reproducible, ignoring the minted row ids.</summary>
    private sealed record SquadRow(
        string FullName,
        string ShortName,
        int BirthGameYear,
        int BirthDayOfYear,
        PreferredFoot PreferredFoot,
        int HeightCm,
        int WeightKg,
        PlayerPosition PrimaryPosition,
        string SecondaryPositionCodes,
        int Potential,
        int Reputation,
        PlayerAttributeSet Attributes,
        long WeeklyWageMinor,
        SquadStatus SquadStatus,
        int StartSeasonNumber,
        int EndSeasonNumber,
        int ConditionBp,
        int MoraleBp);
}

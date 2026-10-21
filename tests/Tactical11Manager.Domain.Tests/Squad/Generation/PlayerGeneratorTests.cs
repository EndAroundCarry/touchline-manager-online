using System.Globalization;
using FluentAssertions;
using FluentAssertions.Execution;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Tests.Squad.Generation;

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
    public void A_generated_squad_has_one_left_and_one_right_midfielder()
    {
        var positions = Generate().Select(member => member.Player.PrimaryPosition).ToList();

        positions.Count(position => position == PlayerPosition.LeftMidfielder).Should().Be(1, "SQ-1");
        positions.Count(position => position == PlayerPosition.RightMidfielder).Should().Be(1, "SQ-1");
    }

    [Fact]
    public void A_wide_midfielder_profile_sits_between_a_full_back_and_a_winger()
    {
        static int Bonus(PlayerPosition position, AttributeName name) =>
            PlayerAttributeProfiles.BonusFor(position, name);

        // More attacking than a full back and less than a winger: finishing and dribbling rise from the full
        // back through the wide midfielder to the winger, and tackling and marking fall.
        foreach (var name in new[] { AttributeName.Finishing, AttributeName.Dribbling })
        {
            Bonus(PlayerPosition.RightBack, name).Should().BeLessThan(Bonus(PlayerPosition.RightMidfielder, name), $"{name}");
            Bonus(PlayerPosition.RightMidfielder, name).Should().BeLessThan(Bonus(PlayerPosition.RightWinger, name), $"{name}");
        }

        foreach (var name in new[] { AttributeName.Tackling, AttributeName.Marking })
        {
            Bonus(PlayerPosition.RightWinger, name).Should().BeLessThan(Bonus(PlayerPosition.RightMidfielder, name), $"{name}");
            Bonus(PlayerPosition.RightMidfielder, name).Should().BeLessThan(Bonus(PlayerPosition.RightBack, name), $"{name}");
        }

        // The delivery and the engine are what he is for.
        Bonus(PlayerPosition.RightMidfielder, AttributeName.Crossing)
            .Should().BeGreaterThanOrEqualTo(Bonus(PlayerPosition.RightWinger, AttributeName.Crossing));
        Bonus(PlayerPosition.RightMidfielder, AttributeName.Stamina)
            .Should().BeGreaterThan(Bonus(PlayerPosition.RightWinger, AttributeName.Stamina));
        Bonus(PlayerPosition.RightMidfielder, AttributeName.WorkRate)
            .Should().BeGreaterThan(Bonus(PlayerPosition.RightWinger, AttributeName.WorkRate));
    }

    [Fact]
    public void The_two_flanks_share_a_wide_midfielder_profile_and_it_is_no_stronger_than_the_others()
    {
        static int Sum(PlayerPosition position) =>
            AttributeNames.All.Sum(name => PlayerAttributeProfiles.BonusFor(position, name));

        Sum(PlayerPosition.RightMidfielder).Should().Be(Sum(PlayerPosition.LeftMidfielder));

        // A profile total bonus is its overall quality against the squad mean: a new position must not be
        // stronger than the best outfield profile nor weaker than the weakest.
        var outfield = PlayerPositions.All
            .Where(position => position is not (PlayerPosition.Goalkeeper
                or PlayerPosition.RightMidfielder
                or PlayerPosition.LeftMidfielder))
            .Select(Sum)
            .ToList();

        Sum(PlayerPosition.RightMidfielder).Should().BeInRange(outfield.Min(), outfield.Max());

        PlayerAttributeProfiles.PhysicalRangeFor(PlayerPosition.LeftMidfielder).Should().NotBeNull();
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
            member.State.AttributeProgress.Should().OnlyContain(
                progress => progress == 0,
                "TRN-10 seeds every attribute's progress at zero");
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
    public void A_players_skills_are_derived_from_his_potential_and_age()
    {
        // Tier 3 keeps the mean well inside 1-20, so the per-skill clamp does not bend the comparison.
        var checkedPlayers = 0;

        for (var clubOrdinal = 0; clubOrdinal < 40; clubOrdinal++)
        {
            foreach (var member in Generate(seed: "potential-first", clubOrdinal: clubOrdinal, tier: 3))
            {
                var age = member.Player.AgeIn(2026);
                var baseAbility = member.Player.Potential + PlayerAttributeProfiles.AgeAbilityAdjustment(age);

                var expected = AttributeNames.All.Average(name => Math.Clamp(
                    baseAbility + PlayerAttributeProfiles.BonusFor(member.Player.PrimaryPosition, name),
                    WorldRuleSet.AttributeMin,
                    WorldRuleSet.AttributeMax));

                var actual = member.Attributes.ToSet().Values.Average();

                // The per-skill jitter averages out over all the skills, so the mean sits close to the ceiling
                // less the years left to develop (TRN-18).
                actual.Should().BeApproximately(expected, 1.0, $"{member.Player.FullName}, age {age}");
                checkedPlayers++;
            }
        }

        checkedPlayers.Should().Be(40 * WorldRuleSet.GeneratorSquadTarget);
    }

    [Fact]
    public void A_young_player_starts_below_a_ceiling_that_is_already_set()
    {
        var young = Enumerable.Range(0, 40)
            .SelectMany(clubOrdinal => Generate(seed: "potential-first", clubOrdinal: clubOrdinal, tier: 1))
            .Where(member => member.Player.AgeIn(2026) <= 20)
            .ToList();

        young.Should().NotBeEmpty();
        young.Average(member => member.Player.Potential - member.Attributes.ToSet().Values.Average())
            .Should().BeGreaterThan(4.0, "a 17 to 20 year old is 4 to 7 points below his ceiling (TRN-18)");
    }

    [Fact]
    public void The_generator_versions_are_named_for_the_run_to_record()
    {
        PlayerGenerator.Version.Should().NotBeNullOrWhiteSpace("FIC-8");
        PlayerAttributeProfiles.Version.Should().NotBeNullOrWhiteSpace("FIC-8");
        PlayerNamePools.Version.Should().NotBeNullOrWhiteSpace("FIC-8");
    }

    [Fact]
    public void A_generated_player_starts_with_the_experience_his_age_gives_him()
    {
        const int gameYear = 2026;

        var players = Enumerable.Range(0, 12)
            .SelectMany(clubOrdinal => Generate(seed: "experience", clubOrdinal: clubOrdinal, gameYear: gameYear))
            .ToList();

        using var scope = new AssertionScope();

        foreach (var member in players)
        {
            var age = member.Player.AgeIn(gameYear);

            member.State.ExperienceBp.Should().Be(PlayerExperience.StartingFor(age), $"age {age}");
            member.State.ExperienceBp.Should().BeLessThanOrEqualTo(PlayerExperience.StartingCapBasisPoints);
        }

        players.Where(member => member.Player.AgeIn(gameYear) <= 18)
            .Should().OnlyContain(member => member.State.ExperienceBp == 0, "17 and 18 year olds start with none");
        players.Select(member => member.State.ExperienceBp).Distinct().Should().HaveCountGreaterThan(5);
    }

    [Fact]
    public void An_emergency_replacement_starts_with_the_experience_his_age_gives_him()
    {
        for (var ordinal = 0; ordinal < 20; ordinal++)
        {
            var member = PlayerGenerator.GenerateEmergencyReplacement(
                Seed,
                "england",
                "ENG",
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                tier: 1,
                Guid.CreateVersion7(),
                seasonNumber: 1,
                gameYear: 2026,
                PlayerPosition.CentreBack,
                ordinal,
                Now);

            member.State.ExperienceBp.Should().Be(PlayerExperience.StartingFor(member.Player.AgeIn(2026)));
        }
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

        GoldenDigest(squad).Should().Be(
            "64306e99164964bf",
            "player-gen-v3 draws potential before the skills and derives the skills from it (TRN-18); "
            + "the old digest was 941aab8c455dbc46");
    }

    [Fact]
    public void A_youth_intake_player_is_17_or_18_with_skills_under_his_ceiling_and_is_reproducible()
    {
        var clubId = Guid.CreateVersion7();

        GeneratedSquadMember Youth(int ordinal) => PlayerGenerator.GenerateYouth(
            Seed, "england", "ENG", Guid.CreateVersion7(), clubId, tier: 1, Guid.CreateVersion7(), seasonNumber: 2, gameYear: 2027,
            PlayerPosition.CentralMidfielder, ordinal, Now);

        var youths = Enumerable.Range(0, 60).Select(Youth).ToList();

        using var scope = new AssertionScope();

        youths.Should().OnlyContain(member => member.Player.AgeIn(2027) >= 17 && member.Player.AgeIn(2027) <= 18);
        youths.Should().OnlyContain(member => member.Attributes.ToSet().IsWithinScale);
        youths.Should().OnlyContain(member => member.Contract.SquadStatus == SquadStatus.Prospect);
        youths.Should().OnlyContain(member => member.Contract.EndSeasonNumber == 2 + WorldRuleSet.ContractMaxSeasons - 1);
        youths.Average(member => member.Attributes.ToSet().Values.Average())
            .Should().BeLessThan(youths.Average(member => (double)member.Player.Potential), "a 17 or 18-year-old starts below his ceiling (TRN-18)");
        youths.Average(member => (double)member.Player.Potential)
            .Should().BeApproximately(WorldRuleSet.YouthPotentialMeanForTier(1), 1.0, "a youth's potential is drawn around the youth mean (TRN-18)");
        Youth(7).Player.FullName.Should().Be(Youth(7).Player.FullName, "the same club and ordinal reproduce the same player (FIC-7)");
        Youth(7).Attributes.ToSet().Should().Be(Youth(7).Attributes.ToSet());
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

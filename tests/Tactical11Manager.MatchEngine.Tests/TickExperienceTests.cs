using System.Reflection;
using FluentAssertions;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Serialization;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>Verifies the experience bonus: a percentage of the skills the tick engine plays with, frozen into the snapshot (experience, Milestone 2).</summary>
public sealed class TickExperienceTests
{
    private static readonly string[] MovementSkills = [nameof(TickPlayerSkills.Pace), nameof(TickPlayerSkills.Acceleration)];

    private static readonly PropertyInfo[] SkillProperties =
    [
        .. typeof(TickPlayerSkills).GetProperties()
            .Where(property => property.PropertyType == typeof(int) && property.GetMethod is { IsStatic: false } && property.SetMethod is not null
                && property.Name is not (nameof(TickPlayerSkills.ConditionShortfall) or nameof(TickPlayerSkills.ExperienceBonusBasisPoints))),
    ];

    [Fact]
    public void The_skills_are_the_twenty_five_attributes_the_engine_plays_with()
    {
        SkillProperties.Should().HaveCount(25);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(20)]
    public void No_bonus_leaves_the_struct_exactly_as_it_was(int sheet)
    {
        var attributes = PlayerAttributesV1.Uniform(sheet);

        foreach (var condition in new[] { 10_000, 7_500, 3_000 })
        {
            var plain = TickPlayerSkills.Tired(attributes, condition);

            TickPlayerSkills.Tired(attributes, condition, 0).Should().Be(plain);
            plain.ExperienceBonusBasisPoints.Should().Be(0);
            plain.Uncurved().ExperienceBonusBasisPoints.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(7_500)]
    [InlineData(2_000)]
    public void A_bonus_is_that_percentage_of_every_skill_but_the_two_speeds_curved_and_uncurved(int condition)
    {
        foreach (var sheet in new[] { 4, 9, 13, 16, 20 })
        {
            var attributes = PlayerAttributesV1.Uniform(sheet);
            var plain = TickPlayerSkills.Tired(attributes, condition);

            foreach (var bonus in new[] { 100, 300, 500 })
            {
                var boosted = TickPlayerSkills.Tired(attributes, condition, bonus);

                boosted.ExperienceBonusBasisPoints.Should().Be(bonus);
                boosted.Condition.Should().Be(plain.Condition);
                Compare(plain, boosted, bonus);
                Compare(plain.Uncurved(), boosted.Uncurved(), bonus);
                boosted.Uncurved().ExperienceBonusBasisPoints.Should().Be(bonus, "the raw twin keeps the bonus");
            }
        }
    }

    [Fact]
    public void A_sheet_20_with_the_top_bonus_is_not_capped_at_the_top_of_the_scale()
    {
        var skills = TickPlayerSkills.Rated(PlayerAttributesV1.Uniform(20), 500);

        skills.Tackling.Should().Be(1_575, "a curved 15.00 plus 5%");
        skills.Uncurved().Tackling.Should().Be(2_100, "a raw 20 plus 5% plays as a 21");
        skills.Uncurved().SetPieces.Should().Be(2_100);
        skills.Uncurved().Reflexes.Should().Be(2_100);
    }

    [Fact]
    public void Pace_and_acceleration_get_no_bonus()
    {
        var plain = TickPlayerSkills.Rated(PlayerAttributesV1.Uniform(18));
        var boosted = TickPlayerSkills.Rated(PlayerAttributesV1.Uniform(18), 500);

        boosted.Pace.Should().Be(plain.Pace);
        boosted.Acceleration.Should().Be(plain.Acceleration);
        boosted.Uncurved().Pace.Should().Be(plain.Uncurved().Pace);
        boosted.Tackling.Should().BeGreaterThan(plain.Tackling);
    }

    [Fact]
    public void Skills_not_built_from_attributes_take_no_bonus()
    {
        TickPlayerSkills.From(PlayerAttributesV1.Uniform(12)).ExperienceBonusBasisPoints.Should().Be(0);
    }

    [Fact]
    public void The_bonus_survives_a_refresh_as_the_player_tires()
    {
        var state = TickTestMatchState.Create(WithHomeBonus(TestMatchFactory.Even(), 300));
        var team = new TickTeam(state.Home);
        var seat = 3;

        team.Skills[seat].ExperienceBonusBasisPoints.Should().Be(300);

        team.Body[seat].Energy -= TickPlayerPhysics.EnergyFull / 2;
        team.RefreshSkills();

        var live = team.LiveConditionOf(seat);

        live.Should().BeLessThan(team.ConditionOf(seat));
        team.Skills[seat].ExperienceBonusBasisPoints.Should().Be(300);
        team.Skills[seat].Should().Be(TickPlayerSkills.Tired(team.Skills[seat].Natural!, live, 300));
    }

    [Fact]
    public void A_bonus_in_the_snapshot_reaches_the_seat_it_was_frozen_for_and_no_other()
    {
        var input = TestMatchFactory.Even();
        var starter = input.Home.Slots.First(slot => slot.SlotNumber == 5).ParticipantId;
        var squad = input.Home.Squad
            .Select(player => player.ParticipantId == starter ? player with { State = player.State with { ExperienceBonusBasisPoints = 200 } } : player)
            .ToList();

        var team = new TickTeam(TickTestMatchState.Create(input with { Home = input.Home with { Squad = squad } }).Home);
        var bonuses = Enumerable.Range(0, team.Count).Select(seat => team.Skills[seat].ExperienceBonusBasisPoints).ToList();

        bonuses.Count(bonus => bonus == 200).Should().Be(1);
        bonuses.Count(bonus => bonus == 0).Should().Be(10);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(501)]
    public void A_bonus_outside_the_range_is_refused(int bonus)
    {
        var input = TestMatchFactory.Even();
        var squad = input.Home.Squad.ToList();

        squad[2] = squad[2] with { State = squad[2].State with { ExperienceBonusBasisPoints = bonus } };

        var act = (input with { Home = input.Home with { Squad = squad } }).Validate;

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*experience bonus*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void A_bonus_at_the_ends_of_the_range_is_accepted(int bonus)
    {
        var act = WithHomeBonus(TestMatchFactory.Even(), bonus).Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void The_hash_is_unchanged_with_no_bonus_and_moves_with_one()
    {
        var input = TestMatchFactory.Even();
        var none = WithHomeBonus(input, 0);
        var some = WithHomeBonus(input, 100);

        CanonicalMatchSerializer.CanonicalText(none).Should().NotContain("experienceBonus");
        CanonicalMatchSerializer.InputHash(none).Should().Be(CanonicalMatchSerializer.InputHash(input));
        CanonicalMatchSerializer.CanonicalText(some).Should().Contain("experienceBonus");
        CanonicalMatchSerializer.InputHash(some).Should().NotBe(CanonicalMatchSerializer.InputHash(input));
        CanonicalMatchSerializer.InputHash(WithHomeBonus(input, 200)).Should().NotBe(CanonicalMatchSerializer.InputHash(some));
    }

    [Fact]
    public void Sides_of_sheet_20_players_with_the_top_bonus_play_full_matches_in_range()
    {
        // Every seat, the goalkeeper and the free-kick taker among them, plays a raw 21 on the dead-ball paths and a 15.75 elsewhere.
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var input = Tick(TestMatchFactory.Even(seed, 20, 20));
            var boosted = input with { Home = WithBonus(input.Home, 500), Away = WithBonus(input.Away, 500) };

            var result = MatchSimulator.Simulate(boosted);

            result.HomeGoals.Should().BeInRange(0, 30);
            result.AwayGoals.Should().BeInRange(0, 30);
            result.OutputHash.Should().NotBeNullOrEmpty();
        }
    }

    private static void Compare(TickPlayerSkills plain, TickPlayerSkills boosted, int bonus)
    {
        foreach (var property in SkillProperties)
        {
            var before = (int)property.GetValue(plain)!;
            var after = (int)property.GetValue(boosted)!;

            if (MovementSkills.Contains(property.Name))
            {
                after.Should().Be(before, property.Name);
            }
            else
            {
                after.Should().Be((int)((((long)before * (10_000 + bonus)) + 5_000) / 10_000), property.Name);
            }
        }
    }

    private static MatchInputV1 WithHomeBonus(MatchInputV1 input, int bonus) => input with { Home = WithBonus(input.Home, bonus) };

    private static MatchSideV1 WithBonus(MatchSideV1 side, int bonus) =>
        side with { Squad = [.. side.Squad.Select(player => player with { State = player.State with { ExperienceBonusBasisPoints = bonus } })] };

    private static MatchInputV1 Tick(MatchInputV1 input) =>
        input with
        {
            EngineVersion = EngineVersions.EngineLabel,
            RuleSetVersion = EngineVersions.RuleSetLabel,
            FormulaConfigurationHash = EngineConfiguration.HashOf(EngineRulesV2.Default, EngineVersions.RuleSetLabel),
        };
}

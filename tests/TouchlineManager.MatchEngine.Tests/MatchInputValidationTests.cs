using FluentAssertions;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The front door: a snapshot that cannot be simulated is refused, by name.
/// </summary>
/// <remarks>
/// Every case here would otherwise produce a plausible-looking but meaningless match. The contract the fuzz
/// suite relies on is "valid input, or a named refusal, never a silent wrong answer", and this is where that is
/// established.
/// </remarks>
public sealed class MatchInputValidationTests
{
    [Fact]
    public void A_well_formed_snapshot_is_accepted()
    {
        var act = TestMatchFactory.Even().Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void A_missing_fixture_identity_is_refused()
    {
        var input = TestMatchFactory.Even() with { FixtureId = Guid.Empty };

        Refuses(input, "fixture identity");
    }

    [Fact]
    public void A_club_playing_itself_is_refused()
    {
        var input = TestMatchFactory.Even();

        var act = (input with { Away = input.Away with { ClubId = input.Home.ClubId } }).Validate;

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*cannot play itself*");
    }

    [Fact]
    public void A_lineup_of_ten_is_refused()
    {
        var input = TestMatchFactory.Even();

        var tenSlots = input.Home.Slots.Take(10).ToList();

        Refuses(input with { Home = input.Home with { Slots = tenSlots } }, "exactly 11");
    }

    [Fact]
    public void A_repeated_slot_number_is_refused()
    {
        var input = TestMatchFactory.Even();

        var slots = input.Home.Slots.ToList();
        slots[5] = slots[5] with { SlotNumber = slots[4].SlotNumber };

        Refuses(input with { Home = input.Home with { Slots = slots } }, "repeats slot number");
    }

    [Fact]
    public void A_slot_naming_a_stranger_is_refused()
    {
        var input = TestMatchFactory.Even();

        var slots = input.Home.Slots.ToList();
        slots[3] = slots[3] with { ParticipantId = TestMatchFactory.Identity(9_999) };

        Refuses(input with { Home = input.Home with { Slots = slots } }, "not in the squad");
    }

    [Fact]
    public void A_repeated_participant_is_refused()
    {
        var input = TestMatchFactory.Even();

        var slots = input.Home.Slots.ToList();
        slots[4] = slots[4] with { ParticipantId = slots[3].ParticipantId };

        Refuses(input with { Home = input.Home with { Slots = slots } }, "two slots");
    }

    [Fact]
    public void The_same_player_in_two_squad_entries_is_refused()
    {
        var input = TestMatchFactory.Even();

        var squad = input.Home.Squad.ToList();
        squad[1] = squad[1] with { ParticipantId = squad[0].ParticipantId };

        Refuses(input with { Home = input.Home with { Squad = squad } }, "more than once");
    }

    [Fact]
    public void A_player_from_another_club_is_refused()
    {
        var input = TestMatchFactory.Even();

        var squad = input.Home.Squad.ToList();
        squad[2] = squad[2] with { ClubId = input.Away.ClubId };

        Refuses(input with { Home = input.Home with { Squad = squad } }, "from another club");
    }

    [Fact]
    public void No_recognised_goalkeeper_is_refused()
    {
        var input = TestMatchFactory.Even();

        var squad = input.Home.Squad.ToList();
        squad[0] = squad[0] with { Position = MatchPosition.CentreBack };

        Refuses(input with { Home = input.Home with { Squad = squad } }, "exactly one recognised goalkeeper");
    }

    [Fact]
    public void Two_recognised_goalkeepers_are_refused()
    {
        var input = TestMatchFactory.Even();

        var squad = input.Home.Squad.ToList();
        squad[1] = squad[1] with { Position = MatchPosition.Goalkeeper };

        Refuses(input with { Home = input.Home with { Squad = squad } }, "exactly one recognised goalkeeper");
    }

    [Fact]
    public void A_slot_off_the_pitch_is_refused()
    {
        var input = TestMatchFactory.Even();

        var slots = input.Home.Slots.ToList();
        slots[2] = slots[2] with { X = 10_001 };

        Refuses(input with { Home = input.Home with { Slots = slots } }, "outside the pitch");
    }

    [Fact]
    public void Two_slots_on_the_same_point_are_refused()
    {
        var input = TestMatchFactory.Even();

        var slots = input.Home.Slots.ToList();
        slots[3] = slots[3] with { X = slots[2].X, Y = slots[2].Y };

        Refuses(input with { Home = input.Home with { Slots = slots } }, "two slots at");
    }

    [Fact]
    public void A_role_that_disagrees_with_its_family_is_refused()
    {
        var input = TestMatchFactory.Even();

        var slots = input.Home.Slots.ToList();
        slots[1] = slots[1] with { Role = MatchRole.Striker };

        Refuses(input with { Home = input.Home with { Slots = slots } }, "not a Defence role");
    }

    [Fact]
    public void An_eighth_substitute_is_refused()
    {
        var input = TestMatchFactory.Even();

        var squad = input.Home.Squad.ToList();
        var extra = squad[^1] with
        {
            ParticipantId = TestMatchFactory.Identity(8_888),
            PlayerId = TestMatchFactory.Identity(9_888),
        };

        squad.Add(extra);

        Refuses(input with { Home = input.Home with { Squad = squad } }, "more than the 7");
    }

    [Fact]
    public void A_side_with_too_few_players_is_refused()
    {
        var input = TestMatchFactory.Even();

        var squad = input.Home.Squad.Take(10).ToList();

        Refuses(input with { Home = input.Home with { Squad = squad } }, "fewer than the 11");
    }

    [Fact]
    public void An_attribute_outside_the_scale_is_refused()
    {
        var values = Enumerable.Repeat(13, MatchAttributeNames.Count).ToArray();
        values[0] = 21;

        var act = () => PlayerAttributesV1.From(values);

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*outside 1..20*");
    }

    [Fact]
    public void The_wrong_number_of_attributes_is_refused()
    {
        var act = () => PlayerAttributesV1.From([1, 2, 3]);

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*exactly 28 attributes*");
    }

    [Fact]
    public void A_snapshot_frozen_for_another_engine_version_is_refused()
    {
        var input = TestMatchFactory.Even() with { EngineVersion = "engine-v0" };

        var act = () => MatchSimulator.Simulate(input);

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*engine-v1*");
    }

    [Fact]
    public void A_snapshot_frozen_for_another_rules_version_is_refused()
    {
        var input = TestMatchFactory.Even() with { RuleSetVersion = "engine-rules-v0" };

        var act = () => MatchSimulator.Simulate(input);

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*engine-rules-v1*");
    }

    [Fact]
    public void A_snapshot_whose_configuration_hash_does_not_match_is_refused()
    {
        // The result would otherwise claim a provenance it does not have, which is worse than a refusal because
        // nobody can detect it afterwards.
        var input = TestMatchFactory.Even() with { FormulaConfigurationHash = new string('a', 64) };

        var act = () => MatchSimulator.Simulate(input);

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*configuration hash*");
    }

    private static void Refuses(MatchInputV1 input, string fragment)
    {
        var act = input.Validate;

        act.Should().Throw<InvalidMatchInputException>().WithMessage($"*{fragment}*");
    }
}

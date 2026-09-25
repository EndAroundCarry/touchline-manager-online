using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The formation presets and the tactics validator (`TAC-1`…`TAC-9`, `INS-10`, `INS-12`, `SQ-4`).
/// </summary>
/// <remarks>
/// The validator is where a manager's saved plan and — in Stage 8 — the AI's default lineup are held to
/// the same rules (`INS-12`), so these tests cover both what it accepts and what it refuses without a
/// database: positions and availability arrive as facts, not as a repository.
/// </remarks>
public sealed class SquadTacticsValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_preset_lays_out_eleven_slots_led_by_a_goalkeeper()
    {
        FormationPresets.All.Should().HaveCount(6, "TAC-1..TAC-6");

        foreach (var preset in FormationPresets.All)
        {
            var because = $"{preset.ToCode()} names eleven valid slots";
            var slots = FormationLayouts.DefaultSlots(preset);

            slots.Should().HaveCount(FormationLayouts.SlotCount, because);
            slots.Select(slot => slot.SlotNumber).Should().BeInAscendingOrder(because);
            slots.Select(slot => slot.SlotNumber)
                .Should().BeEquivalentTo(Enumerable.Range(TacticalSlot.FirstSlotNumber, FormationLayouts.SlotCount));
            slots.Should().ContainSingle(slot => slot.PositionFamily == PositionFamily.Goalkeeper);
            slots[0].PositionFamily.Should().Be(PositionFamily.Goalkeeper, "slot 1 is the goalkeeper");

            slots.Should().OnlyContain(slot => PlayerRoles.FamilyOf(slot.Role) == slot.PositionFamily, "TAC-8");
            slots.Should().OnlyContain(slot =>
                slot.NormalizedX >= WorldRuleSet.SlotCoordinateMin && slot.NormalizedX <= WorldRuleSet.SlotCoordinateMax
                && slot.NormalizedY >= WorldRuleSet.SlotCoordinateMin
                && slot.NormalizedY <= WorldRuleSet.SlotCoordinateMax, "TAC-9");
        }
    }

    [Fact]
    public void A_preset_layout_is_valid_before_anyone_is_picked()
    {
        var validation = TacticalPlanValidator.Validate(Definitions(FormationPreset.FourFourTwo), [], []);

        validation.IsValid.Should().BeTrue("a plan is legal before the manager picks anybody");
        validation.AssignedCount.Should().Be(0);
        validation.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void A_complete_lineup_of_eligible_players_is_valid()
    {
        var players = Enumerable.Range(0, FormationLayouts.SlotCount).Select(_ => Guid.CreateVersion7()).ToList();

        var validation = TacticalPlanValidator.Validate(
            Definitions(FormationPreset.FourThreeThree, players),
            players,
            []);

        validation.IsValid.Should().BeTrue();
        validation.AssignedCount.Should().Be(FormationLayouts.SlotCount);
        validation.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void An_out_of_position_player_is_allowed()
    {
        // Eleven centre backs in a four-four-two: wrong everywhere, but INS-10 makes that a penalty the
        // engine applies rather than a reason to refuse the plan.
        var players = Enumerable.Range(0, FormationLayouts.SlotCount).Select(_ => Guid.CreateVersion7()).ToList();

        var validation = TacticalPlanValidator.Validate(
            Definitions(FormationPreset.FourFourTwo, players),
            players,
            []);

        validation.IsValid.Should().BeTrue("familiarity is a penalty, not a refusal (INS-10)");
    }

    [Fact]
    public void A_half_filled_lineup_is_refused()
    {
        var players = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToList();
        var slots = Definitions(FormationPreset.FourFourTwo);

        for (var index = 0; index < players.Count; index++)
        {
            slots[index] = slots[index] with { AssignedPlayerId = players[index] };
        }

        var validation = TacticalPlanValidator.Validate(slots, players, []);

        validation.IsValid.Should().BeFalse("eleven slots hold nobody or eleven players (SQ-4)");
        validation.AssignedCount.Should().Be(5);
        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.SelectionIncomplete);
    }

    [Fact]
    public void The_same_player_in_two_slots_is_refused()
    {
        var players = Enumerable.Range(0, FormationLayouts.SlotCount).Select(_ => Guid.CreateVersion7()).ToList();
        var slots = Definitions(FormationPreset.FourFourTwo, players);

        // Slots 10 and 11 both name the player who was in slot 10.
        slots[10] = slots[10] with { AssignedPlayerId = players[9] };

        var validation = TacticalPlanValidator.Validate(slots, players, []);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.DuplicatePlayer);
    }

    [Fact]
    public void A_player_who_is_not_selectable_is_refused()
    {
        var players = Enumerable.Range(0, FormationLayouts.SlotCount).Select(_ => Guid.CreateVersion7()).ToList();
        var slots = Definitions(FormationPreset.FourFourTwo, players);

        // Slot 1 names somebody who is not in the club's selectable squad.
        slots[0] = slots[0] with { AssignedPlayerId = Guid.CreateVersion7() };

        var validation = TacticalPlanValidator.Validate(slots, players, []);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.PlayerNotEligible);
    }

    [Fact]
    public void An_unavailable_player_is_refused()
    {
        var players = Enumerable.Range(0, FormationLayouts.SlotCount).Select(_ => Guid.CreateVersion7()).ToList();
        var slots = Definitions(FormationPreset.FourFourTwo, players);

        var validation = TacticalPlanValidator.Validate(slots, players, [players[0]]);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.PlayerUnavailable);
        validation.Issues[0].SlotNumber.Should().Be(TacticalSlot.FirstSlotNumber);
        validation.Issues[0].PlayerId.Should().Be(players[0]);
    }

    [Fact]
    public void Duplicate_slot_numbers_are_refused()
    {
        var slots = Definitions(FormationPreset.FourFourTwo);

        // Renumber slot 2 to slot 1: eleven slots, but two of them are the first.
        slots[1] = slots[1] with { SlotNumber = TacticalSlot.FirstSlotNumber };

        var validation = TacticalPlanValidator.Validate(slots, [], []);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue => issue.Code == TacticalPlanIssueCode.DuplicateSlotNumber);
    }

    [Fact]
    public void A_coordinate_outside_the_pitch_is_refused()
    {
        var slots = Definitions(FormationPreset.FourFourTwo);

        slots[0] = slots[0] with { NormalizedY = WorldRuleSet.SlotCoordinateMax + 1 };

        var validation = TacticalPlanValidator.Validate(slots, [], []);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.CoordinateOutOfBounds);
    }

    [Fact]
    public void Two_slots_on_the_same_point_are_refused()
    {
        var slots = Definitions(FormationPreset.FourFourTwo);

        // Drag slot 11 onto slot 10's spot (TAC-7).
        slots[10] = slots[10] with { NormalizedX = slots[9].NormalizedX, NormalizedY = slots[9].NormalizedY };

        var validation = TacticalPlanValidator.Validate(slots, [], []);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.OverlappingSlots);
    }

    [Fact]
    public void A_role_in_the_wrong_family_is_refused()
    {
        var slots = Definitions(FormationPreset.FourFourTwo);

        // A striker asked to play in goal: the family and the role disagree (TAC-8).
        slots[0] = slots[0] with { Role = PlayerRole.Striker };

        var validation = TacticalPlanValidator.Validate(slots, [], []);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.RoleFamilyMismatch);
    }

    [Fact]
    public void A_plan_with_the_wrong_number_of_slots_is_refused()
    {
        var validation = TacticalPlanValidator.Validate(Definitions(FormationPreset.FourFourTwo).Take(10), [], []);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue => issue.Code == TacticalPlanIssueCode.SlotCount);
    }

    [Fact]
    public void Issue_codes_are_stable_strings()
    {
        TacticalPlanIssueCode.SlotCount.ToCode().Should().Be("SLOT_COUNT");
        TacticalPlanIssueCode.DuplicateSlotNumber.ToCode().Should().Be("DUPLICATE_SLOT_NUMBER");
        TacticalPlanIssueCode.CoordinateOutOfBounds.ToCode().Should().Be("COORDINATE_OUT_OF_BOUNDS");
        TacticalPlanIssueCode.OverlappingSlots.ToCode().Should().Be("OVERLAPPING_SLOTS");
        TacticalPlanIssueCode.RoleFamilyMismatch.ToCode().Should().Be("ROLE_FAMILY_MISMATCH");
        TacticalPlanIssueCode.DuplicatePlayer.ToCode().Should().Be("DUPLICATE_PLAYER");
        TacticalPlanIssueCode.PlayerNotEligible.ToCode().Should().Be("PLAYER_NOT_ELIGIBLE");
        TacticalPlanIssueCode.PlayerUnavailable.ToCode().Should().Be("PLAYER_UNAVAILABLE");
        TacticalPlanIssueCode.SelectionIncomplete.ToCode().Should().Be("SELECTION_INCOMPLETE");
    }

    [Fact]
    public void A_slot_can_be_reshaped_but_not_off_the_pitch()
    {
        var slot = TacticalSlot.Place(
            Guid.CreateVersion7(), Guid.CreateVersion7(), TacticalSlot.FirstSlotNumber,
            PositionFamily.Goalkeeper, PlayerRole.Goalkeeper, 500, 5_000, null, Now);

        slot.Reshape(PositionFamily.Attack, PlayerRole.Striker, 9_000, 5_000, Now.AddMinutes(1));

        slot.PositionFamily.Should().Be(PositionFamily.Attack);
        slot.Role.Should().Be(PlayerRole.Striker);
        slot.NormalizedX.Should().Be(9_000);

        var offPitch = () => slot.Reshape(PositionFamily.Attack, PlayerRole.Striker, 5_000, -1, Now);

        offPitch.Should().Throw<ArgumentOutOfRangeException>("TAC-9");
    }

    /// <summary>Lays out a preset's slots, optionally assigning one player per slot in order.</summary>
    private static List<TacticalSlotDefinition> Definitions(
        FormationPreset preset,
        List<Guid>? players = null) =>
        [.. FormationLayouts.DefaultSlots(preset).Select((slot, index) => new TacticalSlotDefinition(
            slot.SlotNumber,
            slot.PositionFamily,
            slot.Role,
            slot.NormalizedX,
            slot.NormalizedY,
            players is not null && index < players.Count ? players[index] : null))];
}

using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// Tactical plans, slots, and team sheets (`TAC-1`…`TAC-9`, `INS-1`…`INS-11`, `SQ-4`, `CAL-3`).
/// </summary>
public sealed class SquadTacticsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_formation_preset_round_trips_through_its_code()
    {
        FormationPresets.All.Should().HaveCount(6, "TAC-1..TAC-6");

        foreach (var preset in FormationPresets.All)
        {
            FormationPresets.FromCode(preset.ToCode()).Should().Be(preset);
            preset.ToCode().Length.Should().BeLessThanOrEqualTo(FormationPresets.MaxCodeLength);
        }
    }

    [Fact]
    public void Every_team_instruction_round_trips_through_its_code()
    {
        foreach (var value in Enum.GetValues<Mentality>())
        {
            TeamInstructions.MentalityFromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<Tempo>())
        {
            TeamInstructions.TempoFromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<PassingStyle>())
        {
            TeamInstructions.PassingFromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<Width>())
        {
            TeamInstructions.WidthFromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<Pressing>())
        {
            TeamInstructions.PressingFromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<DefensiveLine>())
        {
            TeamInstructions.LineFromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<TacklingStyle>())
        {
            TeamInstructions.TacklingFromCode(value.ToCode()).Should().Be(value);
        }

        foreach (var value in Enum.GetValues<TimeWasting>())
        {
            TeamInstructions.TimeWastingFromCode(value.ToCode()).Should().Be(value);
        }
    }

    [Fact]
    public void A_role_reports_the_family_it_belongs_to()
    {
        PlayerRoles.FamilyOf(PlayerRole.Goalkeeper).Should().Be(PositionFamily.Goalkeeper);
        PlayerRoles.FamilyOf(PlayerRole.WingBack).Should().Be(PositionFamily.Defence);
        PlayerRoles.FamilyOf(PlayerRole.AttackingMidfielder).Should().Be(PositionFamily.Midfield);
        PlayerRoles.FamilyOf(PlayerRole.Striker).Should().Be(PositionFamily.Attack);
    }

    [Fact]
    public void A_plan_carries_its_formation_and_can_be_revised_and_made_default()
    {
        var plan = TacticalPlan.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Home shape",
            FormationPreset.FourThreeThree, Instructions(), isDefault: false, Now);

        plan.IsDefault.Should().BeFalse();
        plan.Version.Should().Be(1);

        plan.MakeDefault(Now.AddMinutes(1));
        plan.IsDefault.Should().BeTrue("INS-11");
        plan.Version.Should().Be(2);

        plan.Revise("Away shape", FormationPreset.FiveThreeTwo, Instructions() with { Mentality = Mentality.Defensive }, Now.AddMinutes(2));
        plan.Name.Should().Be("Away shape");
        plan.FormationPreset.Should().Be(FormationPreset.FiveThreeTwo);
        plan.Instructions.Mentality.Should().Be(Mentality.Defensive);

        plan.RemoveDefault(Now.AddMinutes(3));
        plan.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void A_plan_name_is_required_and_bounded()
    {
        var blank = () => TacticalPlan.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "  ", FormationPreset.FourFourTwo, Instructions(), false, Now);
        var tooLong = () => TacticalPlan.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', TacticalPlan.MaxNameLength + 1), FormationPreset.FourFourTwo, Instructions(), false, Now);

        blank.Should().Throw<ArgumentException>();
        tooLong.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_slot_holds_a_validated_position_and_assignment()
    {
        var slot = TacticalSlot.Place(
            Guid.CreateVersion7(), Guid.CreateVersion7(), TacticalSlot.FirstSlotNumber,
            PositionFamily.Goalkeeper, PlayerRole.Goalkeeper, 5_000, 1_000, assignedPlayerId: null, Now);

        slot.AssignedPlayerId.Should().BeNull("a plan is legal before the manager picks anybody");

        var playerId = Guid.CreateVersion7();
        slot.Assign(playerId, Now.AddMinutes(1));
        slot.AssignedPlayerId.Should().Be(playerId);

        slot.MoveTo(4_000, 2_000, Now.AddMinutes(2));
        slot.NormalizedX.Should().Be(4_000);

        slot.ChangeRole(PlayerRole.CentreBack, Now.AddMinutes(3));
        slot.Role.Should().Be(PlayerRole.CentreBack);
    }

    [Fact]
    public void A_slot_number_and_a_coordinate_outside_their_bounds_are_rejected()
    {
        var badSlot = () => TacticalSlot.Place(
            Guid.CreateVersion7(), Guid.CreateVersion7(), TacticalSlot.LastSlotNumber + 1,
            PositionFamily.Attack, PlayerRole.Striker, 0, 0, null, Now);
        var badX = () => TacticalSlot.Place(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            PositionFamily.Attack, PlayerRole.Striker, WorldRuleSet.SlotCoordinateMax + 1, 0, null, Now);
        var badY = () => TacticalSlot.Place(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            PositionFamily.Attack, PlayerRole.Striker, 0, -1, null, Now);

        badSlot.Should().Throw<ArgumentOutOfRangeException>("a pitch has eleven slots (TAC-8)");
        badX.Should().Throw<ArgumentOutOfRangeException>("TAC-9");
        badY.Should().Throw<ArgumentOutOfRangeException>("TAC-9");
    }

    [Fact]
    public void A_draft_sheet_locks_once_and_a_locked_sheet_cannot_be_edited()
    {
        var sheet = FixtureTeamSheet.Draft(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            tacticalPlanVersion: 3, Now);

        sheet.IsEditable.Should().BeTrue();

        sheet.Rebase(sheet.TacticalPlanId, 4, Now.AddMinutes(1));
        sheet.TacticalPlanVersion.Should().Be(4);

        sheet.Lock(Now.AddMinutes(2));
        sheet.Status.Should().Be(TeamSheetStatus.Locked, "CAL-3");
        sheet.LockedAt.Should().Be(Now.AddMinutes(2));

        var rebase = () => sheet.Rebase(sheet.TacticalPlanId, 5, Now.AddMinutes(3));
        var lockAgain = () => sheet.Lock(Now.AddMinutes(4));

        rebase.Should().Throw<InvalidOperationException>();
        lockAgain.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_team_sheet_entry_takes_a_slot_that_matches_its_designation()
    {
        var starter = TeamSheetEntry.Select(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            TeamSheetDesignation.Starter, slotNumber: 11, roleOverride: null, Now);

        starter.Designation.Should().Be(TeamSheetDesignation.Starter);

        var substitute = TeamSheetEntry.Select(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            TeamSheetDesignation.Substitute, slotNumber: WorldRuleSet.TeamSheetStarters + 1,
            roleOverride: PlayerRole.Winger, Now);

        substitute.RoleOverride.Should().Be(PlayerRole.Winger);

        var wrongStart = () => TeamSheetEntry.Select(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            TeamSheetDesignation.Starter, slotNumber: 12, roleOverride: null, Now);
        var wrongBench = () => TeamSheetEntry.Select(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            TeamSheetDesignation.Substitute, slotNumber: 1, roleOverride: null, Now);
        var tooMany = () => TeamSheetEntry.Select(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            TeamSheetDesignation.Substitute, TeamSheetEntry.LastSlotNumber + 1, roleOverride: null, Now);

        wrongStart.Should().Throw<ArgumentOutOfRangeException>("SQ-4");
        wrongBench.Should().Throw<ArgumentOutOfRangeException>("SQ-4");
        tooMany.Should().Throw<ArgumentOutOfRangeException>("SQ-4");
    }

    private static TeamInstructionSet Instructions() => new()
    {
        Mentality = Mentality.Balanced,
        Tempo = Tempo.Normal,
        Passing = PassingStyle.MixedPassing,
        Width = Width.Normal,
        Pressing = Pressing.MidBlock,
        DefensiveLine = DefensiveLine.Normal,
        Tackling = TacklingStyle.Normal,
        TimeWasting = TimeWasting.Situational,
    };
}

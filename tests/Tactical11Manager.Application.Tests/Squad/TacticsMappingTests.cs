using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Tests.Squad;

/// <summary>
/// A saved plan's response carries the warnings about the placements its match instructions make: a player put where he is not at home, and
/// a player placed whom the same instruction takes off (`MAT-6`, `INS-10`).
/// </summary>
public sealed class TacticsMappingTests
{
    private static readonly Guid Keeper = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Defender = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Midfielder = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid Substitute = Guid.Parse("00000000-0000-0000-0000-000000000012");

    [Fact]
    public void A_placement_out_of_position_and_a_placed_player_who_leaves_are_warned_about_by_player()
    {
        var attackSlots = FormationLayouts.DefaultSlots(FormationPreset.ThreeFourThree)
            .Where(slot => slot.PositionFamily == PositionFamily.Attack)
            .Select(slot => slot.SlotNumber)
            .ToArray();
        var instruction = new MatchInstructionDefinition
        {
            Name = "Go three at the back",
            FromMinute = 60,
            Formation = FormationPreset.ThreeFourThree,
            Overrides =
            [
                new MatchInstructionOverride(Defender, attackSlots[0]),
                new MatchInstructionOverride(Substitute, attackSlots[1]),
                new MatchInstructionOverride(Midfielder, attackSlots[2]),
            ],
            Substitutions =
            [
                new MatchInstructionSubstitution(
                    MatchInstructionOff.Player, Midfielder, null, MatchInstructionOn.Player, Substitute, null),
            ],
        };

        var response = Plan(instruction).ToResponse();

        response.MatchInstructionWarnings.Should().BeEquivalentTo(
        [
            new { Code = "INSTRUCTION_OUT_OF_POSITION", Ordinal = 1, PlayerId = (Guid?)Defender },
            new { Code = "INSTRUCTION_PLACED_PLAYER_LEAVES", Ordinal = 1, PlayerId = (Guid?)Midfielder },
        ]);
    }

    private static TacticsPlanRow Plan(MatchInstructionDefinition instruction)
    {
        var slots = FormationLayouts.DefaultSlots(FormationPreset.FourFourTwo)
            .Select(slot => new TacticsSlotRow(
                slot.SlotNumber,
                slot.PositionFamily,
                slot.Role,
                slot.NormalizedX,
                slot.NormalizedY,
                slot.SlotNumber == 1
                    ? Row(Keeper, PlayerPosition.Goalkeeper)
                    : (slot.SlotNumber == 2
                        ? Row(Defender, PlayerPosition.CentreBack)
                        : (slot.SlotNumber == 6 ? Row(Midfielder, PlayerPosition.CentralMidfielder) : null))))
            .ToList();

        return new TacticsPlanRow(
            Guid.CreateVersion7(),
            "Home",
            FormationPreset.FourFourTwo,
            TeamInstructionSet.Neutral,
            true,
            1,
            slots,
            [new TacticsBenchRow(12, Row(Substitute, PlayerPosition.Striker))])
        {
            MatchInstructions = [instruction],
        };
    }

    private static TacticsAssignedPlayerRow Row(Guid id, PlayerPosition position) =>
        new(id, "Player", "PLY", position, [], false);
}

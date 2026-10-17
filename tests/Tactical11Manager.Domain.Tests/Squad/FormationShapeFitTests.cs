using System.Text.Json;
using FluentAssertions;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>
/// Pins the domain's copy of the shape fit to <c>tests/shared/shape-fit-cases.json</c>, the table the engine's and the web app's copies
/// are held to as well (`MAT-6`).
/// </summary>
public sealed class FormationShapeFitTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();

        foreach (var shapeCase in LoadCases())
        {
            data.Add(shapeCase.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_shared_cases_come_out_as_the_table_says(string name)
    {
        var shapeCase = LoadCases().Single(candidate => candidate.Name == name);

        var fit = FormationShapeFit.Fit(
            [.. shapeCase.Players.Select(PlayerOf)],
            [.. shapeCase.NewSlots.Select(SlotOf)],
            shapeCase.Overrides);

        fit.Should().BeEquivalentTo(shapeCase.Expected);
    }

    [Fact]
    public void The_order_the_caller_lists_players_and_slots_in_changes_nothing()
    {
        var shapeCase = LoadCases().Single(candidate => candidate.Name.StartsWith("four-four-two to", StringComparison.Ordinal));
        var players = shapeCase.Players.Select(PlayerOf).ToList();
        var slots = shapeCase.NewSlots.Select(SlotOf).ToList();

        var first = FormationShapeFit.Fit(players, slots, null);
        var shuffled = FormationShapeFit.Fit([.. players.AsEnumerable().Reverse()], [.. slots.AsEnumerable().Reverse()], null);

        shuffled.Should().BeEquivalentTo(first);
    }

    [Fact]
    public void Two_slots_with_one_number_are_refused()
    {
        var slot = new FormationSlot(2, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 4_000);

        var act = () => FormationShapeFit.Fit([], [slot, slot with { NormalizedX = 1_900 }], null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_whole_squad_fits_into_every_preset_with_nobody_left_over()
    {
        var players = FormationLayouts.DefaultSlots(FormationPreset.FourFourTwo)
            .Select(slot => new ShapeFitPlayer(
                Guid.Parse($"00000000-0000-0000-0000-{slot.SlotNumber:D12}"),
                slot.Role switch
                {
                    PlayerRole.Goalkeeper => PlayerPosition.Goalkeeper,
                    PlayerRole.CentreBack => PlayerPosition.CentreBack,
                    PlayerRole.FullBack => PlayerPosition.RightBack,
                    PlayerRole.WideMidfielder => PlayerPosition.RightMidfielder,
                    PlayerRole.CentralMidfielder => PlayerPosition.CentralMidfielder,
                    _ => PlayerPosition.Striker,
                },
                [],
                slot))
            .ToList();

        foreach (var preset in FormationPresets.All)
        {
            var fit = FormationShapeFit.Fit(players, FormationLayouts.DefaultSlots(preset), null);

            fit.Values.Should().BeEquivalentTo(Enumerable.Range(1, 11), preset.ToCode());
            fit[players[0].Id].Should().Be(1, "the goalkeeper stays in goal");
        }
    }

    [Fact]
    public void The_familiarity_grades_are_the_engines()
    {
        var winger = new ShapeFitPlayer(Guid.NewGuid(), PlayerPosition.RightWinger, [], Slot(PositionFamily.Attack, PlayerRole.Winger));

        FormationShapeFit.FamiliarityOf(winger, Slot(PositionFamily.Attack, PlayerRole.Winger)).Should().Be(FormationShapeFit.Certain);
        FormationShapeFit.FamiliarityOf(winger, Slot(PositionFamily.Attack, PlayerRole.Striker))
            .Should().Be(FormationShapeFit.UnfamiliarRoleBasisPoints, "the right band, the wrong job");
        FormationShapeFit.FamiliarityOf(winger, Slot(PositionFamily.Midfield, PlayerRole.WideMidfielder))
            .Should().Be(FormationShapeFit.SecondaryPositionBasisPoints, "a position the role names covers it");
        FormationShapeFit.FamiliarityOf(winger, Slot(PositionFamily.Defence, PlayerRole.CentreBack))
            .Should().Be(FormationShapeFit.OutOfPositionBasisPoints);
    }

    private static FormationSlot Slot(PositionFamily family, PlayerRole role) => new(0, family, role, 5_000, 5_000);

    private static ShapeFitPlayer PlayerOf(CasePlayer player) => new(
        player.Id,
        Enum.Parse<PlayerPosition>(player.Position),
        [.. player.Secondary.Select(Enum.Parse<PlayerPosition>)],
        new FormationSlot(0, Enum.Parse<PositionFamily>(player.Slot.Family), Enum.Parse<PlayerRole>(player.Slot.Role), player.Slot.X, player.Slot.Y));

    private static FormationSlot SlotOf(CaseSlot slot) =>
        new(slot.Slot, Enum.Parse<PositionFamily>(slot.Family), Enum.Parse<PlayerRole>(slot.Role), slot.X, slot.Y);

    private static List<ShapeCase> LoadCases()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shape-fit-cases.json"));

        return JsonSerializer.Deserialize<CaseFile>(text, Json)!.Cases;
    }

    private sealed record CaseFile(List<ShapeCase> Cases);

    private sealed record ShapeCase(
        string Name,
        List<CasePlayer> Players,
        List<CaseSlot> NewSlots,
        Dictionary<Guid, int> Overrides,
        Dictionary<Guid, int> Expected);

    private sealed record CasePlayer(Guid Id, string Position, List<string> Secondary, CaseStanding Slot);

    private sealed record CaseStanding(string Family, string Role, int X, int Y);

    private sealed record CaseSlot(int Slot, string Family, string Role, int X, int Y);
}

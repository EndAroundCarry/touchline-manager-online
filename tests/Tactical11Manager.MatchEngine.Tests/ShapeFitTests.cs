using System.Text.Json;
using FluentAssertions;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Ratings;
using Tactical11Manager.MatchEngine.Simulation;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// Pins the shape fit: who takes which slot when a side moves into a new formation. The cases are the table
/// <c>tests/shared/shape-fit-cases.json</c>, which the domain's and the web app's copies of the algorithm are held to as well.
/// </summary>
public sealed class ShapeFitTests
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

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

        var fit = ShapeFit.Fit(shapeCase.Players.Select(ActiveOf).ToList(), shapeCase.NewSlots.Select(SlotOf).ToList(), shapeCase.Overrides, Rules);

        fit.Should().BeEquivalentTo(shapeCase.Expected);
    }

    [Fact]
    public void The_same_players_and_shape_always_give_the_same_answer()
    {
        var shapeCase = LoadCases().Single(candidate => candidate.Name.StartsWith("four-four-two to", StringComparison.Ordinal));
        var players = shapeCase.Players.Select(ActiveOf).ToList();
        var slots = shapeCase.NewSlots.Select(SlotOf).ToList();

        var first = ShapeFit.Fit(players, slots, null, Rules);
        var shuffled = ShapeFit.Fit([.. players.AsEnumerable().Reverse()], [.. slots.AsEnumerable().Reverse()], null, Rules);

        shuffled.Should().BeEquivalentTo(first, "the order the caller lists players and slots in changes nothing");
    }

    [Fact]
    public void Every_player_is_placed_in_a_different_slot_when_the_side_is_whole()
    {
        var shapeCase = LoadCases().Single(candidate => candidate.Name.StartsWith("four-four-two to", StringComparison.Ordinal));

        var fit = ShapeFit.Fit(shapeCase.Players.Select(ActiveOf).ToList(), shapeCase.NewSlots.Select(SlotOf).ToList(), null, Rules);

        fit.Should().HaveCount(11);
        fit.Values.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Enumerable.Range(1, 11));
    }

    [Fact]
    public void The_goalkeeper_never_leaves_his_slot()
    {
        foreach (var shapeCase in LoadCases().Where(candidate => candidate.Overrides.Count == 0))
        {
            var keeper = shapeCase.Players.FirstOrDefault(player => player.Position == "Goalkeeper");

            if (keeper is null)
            {
                continue;
            }

            var fit = ShapeFit.Fit(shapeCase.Players.Select(ActiveOf).ToList(), shapeCase.NewSlots.Select(SlotOf).ToList(), null, Rules);

            fit[keeper.Id].Should().Be(1, because: shapeCase.Name);
        }
    }

    [Fact]
    public void A_side_down_to_ten_drops_an_attacking_slot()
    {
        var shapeCase = LoadCases().Single(candidate => candidate.Name.StartsWith("ten men", StringComparison.Ordinal));

        var fit = ShapeFit.Fit(shapeCase.Players.Select(ActiveOf).ToList(), shapeCase.NewSlots.Select(SlotOf).ToList(), null, Rules);

        var empty = shapeCase.NewSlots.Select(slot => slot.Slot).Except(fit.Values).Single();

        shapeCase.NewSlots.Single(slot => slot.Slot == empty).Family.Should().Be("Attack");
    }

    [Fact]
    public void Placing_the_same_player_in_two_slots_is_refused_by_the_runtime()
    {
        var state = TickTestMatchState.Create();
        var one = state.Home.Active[3].Participant.ParticipantId;
        var two = state.Home.Active[4].Participant.ParticipantId;
        var slots = state.Home.Active.Select(active => active.Slot).ToList();

        var act = () => state.Home.Reshape(slots, new Dictionary<Guid, int> { [one] = 4, [two] = 4 }, Rules);

        act.Should().Throw<ArgumentException>();
    }

    private static ActiveSlot ActiveOf(CasePlayer player)
    {
        var slot = new MatchSlotV1
        {
            SlotNumber = 0,
            Family = Enum.Parse<MatchPositionFamily>(player.Slot.Family),
            Role = Enum.Parse<MatchRole>(player.Slot.Role),
            X = player.Slot.X,
            Y = player.Slot.Y,
            ParticipantId = player.Id,
        };

        return new ActiveSlot
        {
            Slot = slot,
            Participant = new MatchParticipantV1
            {
                ParticipantId = player.Id,
                PlayerId = player.Id,
                ClubId = Guid.Empty,
                DisplayName = player.Id.ToString("D"),
                ShirtNumber = 1,
                Position = Enum.Parse<MatchPosition>(player.Position),
                SecondaryPositions = [.. player.Secondary.Select(Enum.Parse<MatchPosition>)],
                Attributes = PlayerAttributesV1.Uniform(13),
                State = PlayerMatchStateV1.Uniform(8_000),
            },
            FamiliarityBasisPoints = EngineRulesV2.Certain,
            Condition = PlayerCondition.From(PlayerMatchStateV1.Uniform(8_000)),
        };
    }

    private static MatchSlotV1 SlotOf(CaseSlot slot) => new()
    {
        SlotNumber = slot.Slot,
        Family = Enum.Parse<MatchPositionFamily>(slot.Family),
        Role = Enum.Parse<MatchRole>(slot.Role),
        X = slot.X,
        Y = slot.Y,
        ParticipantId = Guid.Empty,
    };

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

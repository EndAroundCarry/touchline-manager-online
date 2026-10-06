using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The default bench of a tactical plan: seven substitutes, one of them a goalkeeper (`SQ-4`, `SQ-2`).
/// </summary>
public sealed class TacticalBenchValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_plan_with_no_bench_is_valid()
    {
        var validation = Validate(bench: [], goalkeepers: []);

        validation.IsValid.Should().BeTrue("a plan is a legal template before anybody is picked");
    }

    [Fact]
    public void A_full_bench_with_a_goalkeeper_is_valid()
    {
        var players = Squad(18);

        var validation = Validate(Bench(players.Skip(11)), players, goalkeepers: [players[11]]);

        validation.IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_goalkeeper_may_sit_in_any_bench_place()
    {
        var players = Squad(18);

        var validation = Validate(Bench(players.Skip(11)), players, goalkeepers: [players[17]]);

        validation.IsValid.Should().BeTrue("the bench needs a goalkeeper, not one in a particular place");
    }

    [Fact]
    public void A_bench_of_outfield_players_only_is_refused()
    {
        var players = Squad(18);

        var validation = Validate(Bench(players.Skip(11)), players, goalkeepers: []);

        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.BenchNeedsGoalkeeper);
    }

    [Fact]
    public void A_goalkeeper_who_is_not_on_the_bench_does_not_count()
    {
        var players = Squad(18);

        // The only keeper in the squad is the one starting.
        var validation = Validate(Bench(players.Skip(11)), players, goalkeepers: [players[0]]);

        validation.Issues.Should().ContainSingle().Which.Code.Should().Be(TacticalPlanIssueCode.BenchNeedsGoalkeeper);
    }

    [Fact]
    public void Naming_a_bench_without_saying_who_the_keepers_are_is_refused()
    {
        var players = Squad(18);

        var validation = TacticalPlanValidator.Validate(
            Slots(players),
            players,
            [],
            Bench(players.Skip(11)));

        validation.Issues.Should().Contain(issue => issue.Code == TacticalPlanIssueCode.BenchNeedsGoalkeeper);
    }

    [Fact]
    public void A_half_filled_bench_is_refused_without_also_asking_for_a_keeper()
    {
        var players = Squad(18);

        var validation = Validate(Bench(players.Skip(11).Take(3)), players, goalkeepers: []);

        validation.Issues.Should().ContainSingle()
            .Which.Code.Should().Be(TacticalPlanIssueCode.BenchIncomplete, "the manager is still filling it");
    }

    [Fact]
    public void A_player_cannot_start_and_sit_on_the_bench()
    {
        var players = Squad(18);
        var bench = Bench(players.Skip(11)).ToList();

        bench[0] = bench[0] with { PlayerId = players[3] };

        var validation = Validate(bench, players, goalkeepers: [players[17]]);

        validation.Issues.Should().ContainSingle(issue => issue.Code == TacticalPlanIssueCode.DuplicatePlayer)
            .Which.SlotNumber.Should().Be(TacticalBenchSlot.FirstSlotNumber);
    }

    [Fact]
    public void A_player_cannot_take_two_bench_places()
    {
        var players = Squad(18);
        var bench = Bench(players.Skip(11)).ToList();

        bench[1] = bench[1] with { PlayerId = bench[0].PlayerId };

        var validation = Validate(bench, players, goalkeepers: [players[17]]);

        validation.Issues.Should().Contain(issue =>
            issue.Code == TacticalPlanIssueCode.DuplicatePlayer && issue.SlotNumber == bench[1].SlotNumber);
    }

    [Fact]
    public void An_injured_or_foreign_substitute_is_refused_by_place()
    {
        var players = Squad(18);
        var stranger = Guid.CreateVersion7();
        var bench = Bench(players.Skip(11)).ToList();

        bench[2] = bench[2] with { PlayerId = stranger };

        var validation = TacticalPlanValidator.Validate(
            Slots(players),
            players,
            [players[12]],
            bench,
            [players[17]]);

        validation.Issues.Should().Contain(issue =>
            issue.Code == TacticalPlanIssueCode.PlayerUnavailable && issue.SlotNumber == bench[1].SlotNumber);
        validation.Issues.Should().Contain(issue =>
            issue.Code == TacticalPlanIssueCode.PlayerNotEligible && issue.SlotNumber == bench[2].SlotNumber);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(19)]
    public void A_bench_slot_outside_the_bench_is_refused(int slotNumber)
    {
        var players = Squad(18);
        var bench = Bench(players.Skip(11)).ToList();

        bench[0] = bench[0] with { SlotNumber = slotNumber };

        var validation = Validate(bench, players, goalkeepers: [players[17]]);

        validation.Issues.Should().Contain(issue => issue.Code == TacticalPlanIssueCode.BenchSlotNumber);
    }

    [Fact]
    public void Two_substitutes_cannot_share_a_bench_place()
    {
        var players = Squad(18);
        var bench = Bench(players.Skip(11)).ToList();

        bench[1] = bench[1] with { SlotNumber = bench[0].SlotNumber };

        var validation = Validate(bench, players, goalkeepers: [players[17]]);

        validation.Issues.Should().Contain(issue => issue.Code == TacticalPlanIssueCode.BenchSlotNumber);
    }

    [Fact]
    public void The_bench_issue_codes_are_stable_strings()
    {
        TacticalPlanIssueCode.BenchSlotNumber.ToCode().Should().Be("BENCH_SLOT_NUMBER");
        TacticalPlanIssueCode.BenchIncomplete.ToCode().Should().Be("BENCH_INCOMPLETE");
        TacticalPlanIssueCode.BenchNeedsGoalkeeper.ToCode().Should().Be("BENCH_NEEDS_GOALKEEPER");
    }

    [Fact]
    public void A_bench_place_numbers_follow_the_starters_and_refuse_anything_else()
    {
        TacticalBenchSlot.FirstSlotNumber.Should().Be(12);
        TacticalBenchSlot.LastSlotNumber.Should().Be(18);

        var place = TacticalBenchSlot.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), 12, Guid.CreateVersion7(), Now);

        place.Version.Should().Be(1);

        var act = () => TacticalBenchSlot.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), 11, Guid.CreateVersion7(), Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Naming_the_same_substitute_again_does_not_touch_the_place()
    {
        var player = Guid.CreateVersion7();
        var place = TacticalBenchSlot.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), 13, player, Now);

        place.Assign(player, Now.AddMinutes(5));

        place.Version.Should().Be(1);
        place.UpdatedAt.Should().Be(Now);

        place.Assign(Guid.CreateVersion7(), Now.AddMinutes(5));

        place.Version.Should().Be(2);
    }

    private static TacticalPlanValidation Validate(
        IEnumerable<TacticalBenchDefinition> bench,
        IReadOnlyList<Guid>? players = null,
        IReadOnlyCollection<Guid>? goalkeepers = null)
    {
        players ??= Squad(18);

        return TacticalPlanValidator.Validate(Slots(players), players, [], bench, goalkeepers ?? []);
    }

    private static List<Guid> Squad(int size) =>
        [.. Enumerable.Range(0, size).Select(_ => Guid.CreateVersion7())];

    /// <summary>The first eleven players fill a four-four-two.</summary>
    private static List<TacticalSlotDefinition> Slots(IReadOnlyList<Guid> players) =>
        [.. FormationLayouts.DefaultSlots(FormationPreset.FourFourTwo)
            .Select(slot => new TacticalSlotDefinition(
                slot.SlotNumber,
                slot.PositionFamily,
                slot.Role,
                slot.NormalizedX,
                slot.NormalizedY,
                players[slot.SlotNumber - 1]))];

    /// <summary>Names the given players for places 12 onwards.</summary>
    private static List<TacticalBenchDefinition> Bench(IEnumerable<Guid> players) =>
        [.. players.Select((player, index) => new TacticalBenchDefinition(TacticalBenchSlot.FirstSlotNumber + index, player))];
}

using FluentAssertions;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The fixture team-sheet validator (`SQ-4`, `SQ-9`, `TRN-12`).
/// </summary>
/// <remarks>
/// The same pure rule the prepare screen saves through and the snapshot builder will repair through at lock
/// time, so what is worth pinning is that a legal side passes and each illegal one is named rather than
/// silently accepted.
/// </remarks>
public sealed class FixtureTeamSheetValidatorTests
{
    private static readonly Guid[] Squad = [.. Enumerable.Range(0, 25).Select(_ => Guid.CreateVersion7())];

    [Fact]
    public void A_side_of_eleven_starters_and_a_full_bench_is_valid()
    {
        // Slots 12–18 are exactly seven, so a distinct set of slot numbers cannot name more than seven
        // substitutes: the bench's size is bounded by the slot range and needs no rule of its own (SQ-4).
        var selection = Starters().Concat(Substitutes(12, 7)).ToList();

        var validation = Validate(selection);

        validation.IsValid.Should().BeTrue();
        validation.StarterCount.Should().Be(11);
        validation.SubstituteCount.Should().Be(7);
        validation.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Eleven_starters_with_no_bench_is_valid()
    {
        var validation = Validate([.. Starters()]);

        validation.IsValid.Should().BeTrue("up to seven substitutes means a bench is optional (SQ-4)");
        validation.SubstituteCount.Should().Be(0);
    }

    [Fact]
    public void A_short_starting_eleven_is_refused()
    {
        var selection = Starters().Take(10).ToList();

        var validation = Validate(selection);

        validation.IsValid.Should().BeFalse();
        validation.StarterCount.Should().Be(10);
        validation.Issues.Should().Contain(issue => issue.Code == TeamSheetIssueCode.SelectionIncomplete);
    }

    [Fact]
    public void A_slot_number_outside_the_sheet_range_is_refused()
    {
        var selection = Starters();
        selection.Add(new TeamSheetSelection(19, Squad[20]));

        var validation = Validate(selection);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue =>
            issue.Code == TeamSheetIssueCode.SlotNumber && issue.SlotNumber == 19);
    }

    [Fact]
    public void Two_entries_on_one_slot_are_refused()
    {
        var selection = Starters();
        selection.Add(new TeamSheetSelection(11, Squad[21]));

        var validation = Validate(selection);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue =>
            issue.Code == TeamSheetIssueCode.DuplicateSlotNumber && issue.SlotNumber == 11);
    }

    [Fact]
    public void One_player_in_two_places_is_refused()
    {
        var selection = Starters();
        selection.Add(new TeamSheetSelection(12, selection[0].PlayerId));

        var validation = Validate(selection);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue =>
            issue.Code == TeamSheetIssueCode.DuplicatePlayer && issue.SlotNumber == 12);
    }

    [Fact]
    public void A_player_who_is_not_selectable_is_refused()
    {
        var selection = Starters();
        selection[0] = new TeamSheetSelection(1, Guid.CreateVersion7());

        var validation = Validate(selection);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue =>
            issue.Code == TeamSheetIssueCode.PlayerNotEligible && issue.SlotNumber == 1);
    }

    [Fact]
    public void An_unavailable_player_is_refused()
    {
        var selection = Starters();
        var unavailable = selection[4].PlayerId;

        var validation = FixtureTeamSheetValidator.Validate(
            selection,
            Squad,
            [unavailable]);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue =>
            issue.Code == TeamSheetIssueCode.PlayerUnavailable
            && issue.SlotNumber == 5
            && issue.PlayerId == unavailable);
    }

    [Fact]
    public void Issues_are_ordered_by_slot()
    {
        var selection = Starters();
        selection[7] = new TeamSheetSelection(8, Guid.CreateVersion7());
        selection[2] = new TeamSheetSelection(3, Guid.CreateVersion7());

        var validation = Validate(selection);

        var slotNumbers = validation.Issues
            .Where(issue => issue.SlotNumber is not null)
            .Select(issue => issue.SlotNumber!.Value)
            .ToList();

        slotNumbers.Should().BeInAscendingOrder("a screen draws the issues in slot order");
    }

    private static TeamSheetValidation Validate(IEnumerable<TeamSheetSelection> selection) =>
        FixtureTeamSheetValidator.Validate(selection, Squad, []);

    private static List<TeamSheetSelection> Starters() =>
        [.. Enumerable.Range(1, 11).Select(slot => new TeamSheetSelection(slot, Squad[slot - 1]))];

    private static IEnumerable<TeamSheetSelection> Substitutes(int first, int count) =>
        Enumerable.Range(first, count).Select(slot => new TeamSheetSelection(slot, Squad[slot - 1]));
}

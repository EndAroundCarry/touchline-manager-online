using FluentAssertions;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>
/// A plan's match instructions: the entity that stores them and the validator every club is held to (`MAT-6`, `INS-12`).
/// </summary>
public sealed class MatchInstructionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Starter = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static readonly Guid OtherStarter = Guid.Parse("00000000-0000-0000-0000-000000000011");

    private static readonly Guid Substitute = Guid.Parse("00000000-0000-0000-0000-000000000012");

    private static readonly Guid Stranger = Guid.Parse("00000000-0000-0000-0000-000000000099");

    [Fact]
    public void Every_code_round_trips_and_fits_its_column()
    {
        foreach (var value in Enum.GetValues<MatchInstructionScore>())
        {
            MatchInstructionCodes.ScoreFromCode(value.ToCode()).Should().Be(value);
            value.ToCode().Length.Should().BeLessThanOrEqualTo(MatchInstructionCodes.MaxCodeLength);
        }

        foreach (var value in Enum.GetValues<MatchInstructionEvent>())
        {
            MatchInstructionCodes.EventFromCode(value.ToCode()).Should().Be(value);
            value.ToCode().Length.Should().BeLessThanOrEqualTo(MatchInstructionCodes.MaxCodeLength);
        }

        foreach (var value in Enum.GetValues<MatchInstructionOff>())
        {
            MatchInstructionCodes.OffFromCode(value.ToCode()).Should().Be(value);
            value.ToCode().Length.Should().BeLessThanOrEqualTo(MatchInstructionCodes.MaxCodeLength);
        }

        foreach (var value in Enum.GetValues<MatchInstructionOn>())
        {
            MatchInstructionCodes.OnFromCode(value.ToCode()).Should().Be(value);
            value.ToCode().Length.Should().BeLessThanOrEqualTo(MatchInstructionCodes.MaxCodeLength);
        }

        foreach (var code in Enum.GetValues<MatchInstructionIssueCode>())
        {
            code.ToCode().Should().NotBeNullOrEmpty();
        }

        foreach (var code in Enum.GetValues<MatchInstructionWarningCode>())
        {
            code.ToCode().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void An_unknown_code_is_refused()
    {
        var act = () => MatchInstructionCodes.EventFromCode("goal_scored");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_instruction_keeps_everything_it_was_given_through_its_stored_form()
    {
        var definition = Full();

        var instruction = MatchInstruction.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), 3, definition, Now);

        instruction.Ordinal.Should().Be(3);
        instruction.CreatedAt.Should().Be(Now);
        instruction.Definition.Should().BeEquivalentTo(definition);
        instruction.OverridesJson.Should().Contain(OtherStarter.ToString("D"));
        instruction.SubstitutionsJson.Should().Contain("most_tired_in_family");
    }

    [Fact]
    public void Revising_an_instruction_keeps_its_place_and_identity()
    {
        var planId = Guid.CreateVersion7();
        var instruction = MatchInstruction.Place(Guid.CreateVersion7(), planId, 2, Full(), Now);
        var id = instruction.Id;

        instruction.Revise(Plain("Late push") with { FromMinute = 80 }, Now.AddMinutes(5));

        instruction.Id.Should().Be(id);
        instruction.Ordinal.Should().Be(2);
        instruction.PlanId.Should().Be(planId);
        instruction.Name.Should().Be("Late push");
        instruction.FromMinute.Should().Be(80);
        instruction.Overrides.Should().BeEmpty();
        instruction.Substitutions.Should().BeEmpty();
        instruction.Formation.Should().BeNull();
        instruction.CreatedAt.Should().Be(Now);
        instruction.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void An_instruction_starts_at_the_first_place_of_the_list()
    {
        var act = () => MatchInstruction.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, Plain("Push"), Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_plan_has_the_assistant_on_unless_told_otherwise_and_keeps_the_setting_when_revised_without_one()
    {
        var plan = TacticalPlan.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Plan", FormationPreset.FourFourTwo, TeamInstructionSet.Neutral, true, Now);

        plan.AssistantFatigueSubstitutions.Should().BeTrue("every plan made before match instructions played that way");

        plan.Revise("Plan", FormationPreset.FourFourTwo, TeamInstructionSet.Neutral, Now.AddMinutes(1), assistantFatigueSubstitutions: false);
        plan.AssistantFatigueSubstitutions.Should().BeFalse();
        plan.Version.Should().Be(2);

        plan.Revise("Plan", FormationPreset.FourFourTwo, TeamInstructionSet.Neutral, Now.AddMinutes(2));
        plan.AssistantFatigueSubstitutions.Should().BeFalse("a revision that says nothing keeps the setting");
        plan.Version.Should().Be(3, "every revision moves the version, so the entity tag covers the instructions too");
    }

    [Fact]
    public void A_plan_can_be_made_with_the_assistant_off()
    {
        var plan = TacticalPlan.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Plan", FormationPreset.FourFourTwo, TeamInstructionSet.Neutral, true, Now,
            assistantFatigueSubstitutions: false);

        plan.AssistantFatigueSubstitutions.Should().BeFalse();
    }

    [Fact]
    public void A_complete_set_of_instructions_is_valid()
    {
        var result = Validate(Full());

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void No_instructions_is_valid()
    {
        MatchInstructionValidator.Validate([], [], []).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_plan_holds_at_most_ten_instructions()
    {
        var eleven = Enumerable.Range(1, 11).Select(index => Plain($"Rule {index}")).ToList();

        var result = MatchInstructionValidator.Validate(eleven, [Starter], [Substitute]);

        result.Issues.Should().ContainSingle(issue => issue.Code == MatchInstructionIssueCode.TooManyInstructions && issue.Ordinal == null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string name) =>
        IssuesOf(Plain(name)).Should().ContainSingle(issue => issue.Code == MatchInstructionIssueCode.NameInvalid && issue.Ordinal == 1);

    [Fact]
    public void A_name_is_at_most_forty_eight_characters()
    {
        IssuesOf(Plain(new string('a', MatchInstruction.MaxNameLength))).Should().BeEmpty();
        IssuesOf(Plain(new string('a', MatchInstruction.MaxNameLength + 1))).Should()
            .ContainSingle(issue => issue.Code == MatchInstructionIssueCode.NameInvalid);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(91, null)]
    [InlineData(60, 59)]
    [InlineData(60, 91)]
    public void Minutes_outside_the_match_or_backwards_are_refused(int from, int? until) =>
        IssuesOf(Plain("Push") with { FromMinute = from, UntilMinute = until }).Should()
            .ContainSingle(issue => issue.Code == MatchInstructionIssueCode.MinuteRange);

    [Theory]
    [InlineData(0, null)]
    [InlineData(90, null)]
    [InlineData(60, 60)]
    [InlineData(0, 90)]
    public void Minutes_inside_the_match_are_accepted(int from, int? until) =>
        IssuesOf(Plain("Push") with { FromMinute = from, UntilMinute = until }).Should().BeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void A_goal_margin_outside_one_to_five_is_refused(int margin) =>
        IssuesOf(Plain("Push") with { ScoreMargin = margin }).Should()
            .ContainSingle(issue => issue.Code == MatchInstructionIssueCode.ScoreMargin);

    [Theory]
    [InlineData(MatchInstructionEvent.None, null, null, true)]
    [InlineData(MatchInstructionEvent.None, 5_000, null, false)]
    [InlineData(MatchInstructionEvent.None, null, PositionFamily.Defence, false)]
    [InlineData(MatchInstructionEvent.OwnSentOff, null, null, true)]
    [InlineData(MatchInstructionEvent.OpponentSentOff, null, PositionFamily.Attack, false)]
    [InlineData(MatchInstructionEvent.OwnBooked, null, null, true)]
    [InlineData(MatchInstructionEvent.OwnBooked, null, PositionFamily.Defence, true)]
    [InlineData(MatchInstructionEvent.OwnBooked, 5_000, null, false)]
    [InlineData(MatchInstructionEvent.PlayingBadly, null, null, false)]
    [InlineData(MatchInstructionEvent.PlayingBadly, 5_200, null, true)]
    [InlineData(MatchInstructionEvent.PlayingBadly, null, PositionFamily.Attack, true)]
    [InlineData(MatchInstructionEvent.PlayingBadly, 10_001, null, false)]
    [InlineData(MatchInstructionEvent.Tired, null, null, false)]
    [InlineData(MatchInstructionEvent.Tired, 6_000, PositionFamily.Midfield, true)]
    [InlineData(MatchInstructionEvent.Tired, -1, null, false)]
    [InlineData(MatchInstructionEvent.Tired, 10_001, null, false)]
    [InlineData(MatchInstructionEvent.OpponentMentalityAtLeast, null, null, false)]
    [InlineData(MatchInstructionEvent.OpponentMentalityAtLeast, 4, null, true)]
    [InlineData(MatchInstructionEvent.OpponentMentalityAtMost, 0, null, true)]
    [InlineData(MatchInstructionEvent.OpponentMentalityAtMost, 5, null, false)]
    [InlineData(MatchInstructionEvent.OpponentMentalityAtMost, 2, PositionFamily.Attack, false)]
    public void An_event_has_the_parameters_its_kind_needs_and_none_it_does_not(
        MatchInstructionEvent kind,
        int? threshold,
        PositionFamily? family,
        bool valid)
    {
        var issues = IssuesOf(Plain("Event") with { Event = kind, EventThreshold = threshold, EventFamily = family });

        if (valid)
        {
            issues.Should().BeEmpty();
        }
        else
        {
            issues.Should().ContainSingle(issue => issue.Code == MatchInstructionIssueCode.EventParameter);
        }
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(3)]
    public void The_mentality_moves_by_at_most_two_steps(int steps) =>
        IssuesOf(Plain("Push") with { Changes = new MatchInstructionChanges { MentalitySteps = steps } }).Should()
            .ContainSingle(issue => issue.Code == MatchInstructionIssueCode.MentalitySteps);

    [Fact]
    public void An_instruction_makes_at_most_five_substitutions()
    {
        var six = Enumerable.Repeat(Swap(), 6).ToList();

        IssuesOf(Plain("Changes") with { Substitutions = six }).Should()
            .ContainSingle(issue => issue.Code == MatchInstructionIssueCode.TooManySubstitutions);
    }

    [Fact]
    public void A_named_player_to_take_off_must_be_in_the_lineup_and_one_to_bring_on_on_the_bench()
    {
        var off = Plain("Swap") with
        {
            Substitutions = [Swap() with { OffPlayerId = Stranger }],
        };
        var on = Plain("Swap") with
        {
            Substitutions = [Swap() with { OnPlayerId = Starter }],
        };

        IssuesOf(off).Should().ContainSingle(issue =>
            issue.Code == MatchInstructionIssueCode.OffPlayerNotInLineup && issue.PlayerId == Stranger);
        IssuesOf(on).Should().ContainSingle(issue =>
            issue.Code == MatchInstructionIssueCode.OnPlayerNotOnBench && issue.PlayerId == Starter);
    }

    [Fact]
    public void A_substitution_that_names_nobody_for_a_named_selector_is_refused()
    {
        var missing = Plain("Swap") with
        {
            Substitutions = [Swap() with { OffPlayerId = null }, Swap() with { OnPlayerId = null }],
        };

        IssuesOf(missing).Should().HaveCount(2).And.OnlyContain(issue => issue.Code == MatchInstructionIssueCode.SubstitutionSelector);
    }

    [Theory]
    [InlineData(MatchInstructionOff.MostTiredInFamily)]
    [InlineData(MatchInstructionOff.LowestRatedInFamily)]
    public void A_family_selector_needs_its_family(MatchInstructionOff selector)
    {
        var substitution = new MatchInstructionSubstitution(selector, null, null, MatchInstructionOn.Player, Substitute, null);

        IssuesOf(Plain("Swap") with { Substitutions = [substitution] }).Should()
            .ContainSingle(issue => issue.Code == MatchInstructionIssueCode.SubstitutionSelector);
    }

    [Fact]
    public void A_best_fit_replacement_needs_its_family()
    {
        var substitution = new MatchInstructionSubstitution(
            MatchInstructionOff.Player, Starter, null, MatchInstructionOn.BestFitInFamily, null, null);

        IssuesOf(Plain("Swap") with { Substitutions = [substitution] }).Should()
            .ContainSingle(issue => issue.Code == MatchInstructionIssueCode.SubstitutionSelector);
    }

    [Theory]
    [InlineData(MatchInstructionEvent.OwnBooked, true)]
    [InlineData(MatchInstructionEvent.PlayingBadly, true)]
    [InlineData(MatchInstructionEvent.Tired, true)]
    [InlineData(MatchInstructionEvent.None, false)]
    [InlineData(MatchInstructionEvent.OwnSentOff, false)]
    [InlineData(MatchInstructionEvent.OpponentSentOff, false)]
    public void The_player_the_event_is_about_can_only_be_taken_off_when_the_event_is_about_somebody(MatchInstructionEvent kind, bool allowed)
    {
        var instruction = Plain("Swap") with
        {
            Event = kind,
            EventThreshold = kind is MatchInstructionEvent.Tired or MatchInstructionEvent.PlayingBadly ? 5_000 : null,
            Substitutions =
            [
                new MatchInstructionSubstitution(
                    MatchInstructionOff.Subject, null, null, MatchInstructionOn.BestFitInFamily, null, PositionFamily.Midfield),
            ],
        };

        var issues = IssuesOf(instruction);

        if (allowed)
        {
            issues.Should().BeEmpty();
        }
        else
        {
            issues.Should().ContainSingle(issue => issue.Code == MatchInstructionIssueCode.SubstitutionSelector);
        }
    }

    [Fact]
    public void Placements_need_a_new_shape_lineup_players_outfield_slots_and_no_repeats()
    {
        var noShape = Full() with { Formation = null };
        var stranger = Full() with { Overrides = [new MatchInstructionOverride(Stranger, 5)] };
        var goalSlot = Full() with { Overrides = [new MatchInstructionOverride(Starter, 1)] };
        var pastEleven = Full() with { Overrides = [new MatchInstructionOverride(Starter, 12)] };
        var samePlayer = Full() with { Overrides = [new MatchInstructionOverride(Starter, 5), new MatchInstructionOverride(Starter, 6)] };
        var sameSlot = Full() with { Overrides = [new MatchInstructionOverride(Starter, 5), new MatchInstructionOverride(OtherStarter, 5)] };

        foreach (var instruction in new[] { noShape, stranger, goalSlot, pastEleven, samePlayer, sameSlot })
        {
            IssuesOf(instruction).Should().Contain(issue => issue.Code == MatchInstructionIssueCode.OverrideInvalid);
        }
    }

    [Fact]
    public void A_placement_may_name_a_bench_player_the_same_instruction_brings_on_by_name()
    {
        var comesOn = Full() with { Overrides = [new MatchInstructionOverride(Substitute, 9)] };

        Validate(comesOn).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_placement_of_a_bench_player_the_instruction_does_not_bring_on_is_refused()
    {
        var stays = Full() with { Substitutions = [Swap() with { OnPlayerId = null, On = MatchInstructionOn.BestFitInFamily, OnFamily = PositionFamily.Attack }], Overrides = [new MatchInstructionOverride(Substitute, 9)] };
        var offBench = Full() with { Substitutions = [Swap() with { OnPlayerId = Stranger }], Overrides = [new MatchInstructionOverride(Stranger, 9)] };

        foreach (var instruction in new[] { stays, offBench })
        {
            IssuesOf(instruction).Should().Contain(issue => issue.Code == MatchInstructionIssueCode.OverrideInvalid && issue.PlayerId == instruction.Overrides[0].PlayerId);
        }
    }

    [Fact]
    public void A_placement_of_a_player_the_same_instruction_takes_off_is_a_warning_not_an_error()
    {
        var leaves = Full() with { Overrides = [new MatchInstructionOverride(Starter, 9)] };

        Validate(leaves).IsValid.Should().BeTrue();
        Validate(leaves).Warnings.Should().ContainSingle()
            .Which.Should().Be(new MatchInstructionWarning(MatchInstructionWarningCode.PlacedPlayerLeaves, 1, Starter));
    }

    [Fact]
    public void A_placement_in_a_slot_the_player_is_not_at_home_in_is_a_warning_when_the_positions_are_known()
    {
        var attackSlots = FormationLayouts.DefaultSlots(FormationPreset.ThreeFourThree).Where(slot => slot.PositionFamily == PositionFamily.Attack).Select(slot => slot.SlotNumber).ToArray();
        var placed = Full() with { Overrides = [new MatchInstructionOverride(OtherStarter, attackSlots[0]), new MatchInstructionOverride(Substitute, attackSlots[1])] };
        var atHome = new Dictionary<Guid, PlayerPositionFacts>
        {
            [OtherStarter] = new(PlayerPosition.CentreBack, []),
            [Substitute] = new(PlayerPosition.Striker, []),
        };
        var covered = new Dictionary<Guid, PlayerPositionFacts>
        {
            [OtherStarter] = new(PlayerPosition.CentreBack, [PlayerPosition.Striker]),
            [Substitute] = new(PlayerPosition.Striker, []),
        };

        MatchInstructionValidator.WarningsOf([placed]).Should().BeEmpty("the positions are not known");
        MatchInstructionValidator.WarningsOf([placed], atHome).Where(warning => warning.Code == MatchInstructionWarningCode.OutOfPosition)
            .Should().ContainSingle().Which.PlayerId.Should().Be(OtherStarter);
        MatchInstructionValidator.WarningsOf([placed], covered).Should().BeEmpty("a secondary position covers the slot");
    }

    [Fact]
    public void An_instruction_that_is_off_warns_of_nothing()
    {
        var leaves = Full() with { IsEnabled = false, Overrides = [new MatchInstructionOverride(Starter, 9)] };

        MatchInstructionValidator.WarningsOf([leaves]).Should().BeEmpty();
    }

    [Fact]
    public void An_issue_names_the_instruction_by_its_place_in_the_list()
    {
        var result = MatchInstructionValidator.Validate([Plain("Fine"), Plain(" "), Plain("Fine")], [Starter], [Substitute]);

        result.Issues.Should().ContainSingle().Which.Ordinal.Should().Be(2);
    }

    [Fact]
    public void More_than_five_substitutions_across_the_enabled_instructions_is_refused_at_the_instruction_that_passes_the_limit()
    {
        var three = Plain("A") with { Substitutions = [Swap(), Swap(), Swap()] };
        var also = Plain("B") with { Substitutions = [Swap(), Swap(), Swap()] };
        var more = Plain("C") with { Substitutions = [Swap()] };

        var result = MatchInstructionValidator.Validate([three, also, more], [Starter], [Substitute]);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().ContainSingle(issue => issue.Code == MatchInstructionIssueCode.TooManySubstitutionsInPlan)
            .Which.Ordinal.Should().Be(2, "the second instruction takes the total from 3 to 6");
    }

    [Fact]
    public void Exactly_five_substitutions_across_the_instructions_is_accepted()
    {
        var three = Plain("A") with { Substitutions = [Swap(), Swap(), Swap()] };
        var two = Plain("B") with { Substitutions = [Swap(), Swap()] };

        MatchInstructionValidator.Validate([three, two], [Starter], [Substitute]).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_instruction_that_is_off_does_not_count_towards_the_substitution_limit()
    {
        var three = Plain("A") with { Substitutions = [Swap(), Swap(), Swap()] };
        var off = Plain("B") with { IsEnabled = false, Substitutions = [Swap(), Swap(), Swap()] };

        MatchInstructionValidator.Validate([three, off], [Starter], [Substitute]).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_instruction_that_changes_nothing_is_a_warning()
    {
        var empty = Plain("Nothing");

        var warnings = MatchInstructionValidator.WarningsOf([empty, Full()]);

        warnings.Should().ContainSingle().Which.Should().Be(new MatchInstructionWarning(MatchInstructionWarningCode.DoesNothing, 1));
    }

    private static List<MatchInstructionIssue> IssuesOf(MatchInstructionDefinition definition) =>
        [.. Validate(definition).Issues];

    private static MatchInstructionValidation Validate(MatchInstructionDefinition definition) =>
        MatchInstructionValidator.Validate([definition], [Starter, OtherStarter], [Substitute]);

    private static MatchInstructionDefinition Plain(string name) => new() { Name = name };

    private static MatchInstructionSubstitution Swap() => new(
        MatchInstructionOff.Player, Starter, null, MatchInstructionOn.Player, Substitute, null);

    private static MatchInstructionDefinition Full() => new()
    {
        Name = "Chase the game",
        FromMinute = 60,
        UntilMinute = 85,
        Score = MatchInstructionScore.Trailing,
        ScoreMargin = 2,
        Event = MatchInstructionEvent.Tired,
        EventFamily = PositionFamily.Midfield,
        EventThreshold = 6_000,
        Formation = FormationPreset.ThreeFourThree,
        Overrides = [new MatchInstructionOverride(OtherStarter, 9)],
        Changes = new MatchInstructionChanges
        {
            Mentality = Mentality.Positive,
            MentalitySteps = 1,
            Tempo = Tempo.High,
            Pressing = Pressing.HighPress,
            CounterAttack = false,
        },
        Substitutions =
        [
            new MatchInstructionSubstitution(
                MatchInstructionOff.MostTiredInFamily, null, PositionFamily.Midfield,
                MatchInstructionOn.BestFitInFamily, null, PositionFamily.Attack),
            Swap(),
        ],
    };
}

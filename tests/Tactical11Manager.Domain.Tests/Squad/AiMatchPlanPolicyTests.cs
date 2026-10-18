using FluentAssertions;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Squad;

/// <summary>
/// The AI club's match plan (`MAT-6`, `INS-12`).
/// </summary>
/// <remarks>
/// A pure function, so no clock or database is needed. What is pinned is what the evaluator and the balance work depend on: the same club always
/// gets the same plan, every plan is one the manager's validator accepts, the temperament moves the minutes and the steps in the direction it
/// names, and no instruction ever loosens what the club already holds.
/// </remarks>
public sealed class AiMatchPlanPolicyTests
{
    private static readonly TeamInstructionSet Neutral = TeamInstructionSet.Neutral;

    [Fact]
    public void The_same_club_gets_the_same_plan()
    {
        var first = AiMatchPlanPolicy.Decide(ClubId(7), FormationPreset.FourFourTwo, Neutral);
        var second = AiMatchPlanPolicy.Decide(ClubId(7), FormationPreset.FourFourTwo, Neutral);

        second.Temperament.Should().Be(first.Temperament);
        second.Instructions.Should().BeEquivalentTo(first.Instructions, options => options.WithStrictOrdering(), "a plan is a pure function of the club (INS-12)");
    }

    [Fact]
    public void Every_plan_is_one_the_managers_validator_accepts_for_every_shape_the_ai_plays()
    {
        foreach (var formation in FormationPresets.AiPool)
        {
            foreach (var index in Enumerable.Range(0, 60))
            {
                var plan = AiMatchPlanPolicy.Decide(ClubId(index), formation, Neutral);
                var validation = MatchInstructionValidator.Validate(plan.Instructions, [], []);

                validation.IsValid.Should().BeTrue("INS-12 holds the AI to the manager's validator ({0}, club {1})", formation.ToCode(), index);
                validation.Warnings.Should().BeEmpty("every instruction the AI writes does something");
                plan.Instructions.Sum(instruction => instruction.Substitutions.Count)
                    .Should().BeLessThanOrEqualTo(MatchInstruction.MatchSubstitutionLimit);
            }
        }
    }

    [Fact]
    public void The_plan_names_families_and_never_players()
    {
        var plan = AiMatchPlanPolicy.Decide(ClubId(3), FormationPreset.FourThreeThree, Neutral);

        plan.Instructions.SelectMany(instruction => instruction.Substitutions).Should().NotBeEmpty();
        plan.Instructions.SelectMany(instruction => instruction.Substitutions).Should().OnlyContain(substitution =>
            substitution.Off != MatchInstructionOff.Player
            && substitution.On != MatchInstructionOn.Player
            && substitution.OffPlayerId == null
            && substitution.OnPlayerId == null);
        plan.Instructions.SelectMany(instruction => instruction.Overrides).Should().BeEmpty();
    }

    [Fact]
    public void The_five_instructions_are_the_assistants_five()
    {
        var plan = AiMatchPlanPolicy.Decide(ClubId(1), FormationPreset.FourFourTwo, Neutral);

        plan.Instructions.Select(instruction => instruction.Name).Should().Equal(
            "Protect a two-goal lead",
            "Hold a narrow lead",
            "Chase the game",
            "Go all out",
            "Settle down with ten men");

        var protect = plan.Instructions[0];
        var hold = plan.Instructions[1];
        var chase = plan.Instructions[2];
        var allOut = plan.Instructions[3];
        var tenMen = plan.Instructions[4];

        protect.Score.Should().Be(MatchInstructionScore.Leading);
        protect.ScoreMargin.Should().Be(2);
        protect.Changes.MentalitySteps.Should().BeNegative("a lead is protected by stepping towards defence");

        hold.Score.Should().Be(MatchInstructionScore.Leading);
        hold.ScoreMargin.Should().Be(1);
        hold.Substitutions.Should().ContainSingle().Which.OffFamily.Should().Be(PositionFamily.Attack, "the most tired attacker makes way");

        chase.Score.Should().Be(MatchInstructionScore.Trailing);
        chase.Changes.MentalitySteps.Should().BePositive();
        chase.Substitutions.Should().ContainSingle().Which.Should().Match<MatchInstructionSubstitution>(substitution =>
            substitution.OffFamily == PositionFamily.Midfield && substitution.OnFamily == PositionFamily.Attack);

        allOut.Score.Should().Be(MatchInstructionScore.Trailing);
        allOut.Changes.Mentality.Should().Be(Mentality.Attacking);
        allOut.Changes.Pressing.Should().Be(Pressing.HighPress);
        allOut.FromMinute.Should().BeGreaterThan(chase.FromMinute, "the all-out push comes after the first chase");

        tenMen.Event.Should().Be(MatchInstructionEvent.OwnSentOff);
        tenMen.Changes.MentalitySteps.Should().BeNegative();
    }

    [Fact]
    public void Temperaments_move_the_minutes_and_the_steps_the_way_they_are_named()
    {
        var plans = Enumerable.Range(0, 300)
            .Select(index => AiMatchPlanPolicy.Decide(ClubId(index), FormationPreset.FourFourTwo, Neutral))
            .ToList();

        var cautious = plans.First(plan => plan.Temperament == AiTemperament.Cautious).Instructions;
        var balanced = plans.First(plan => plan.Temperament == AiTemperament.Balanced).Instructions;
        var bold = plans.First(plan => plan.Temperament == AiTemperament.Bold).Instructions;

        cautious[0].FromMinute.Should().BeLessThan(balanced[0].FromMinute).And.BeLessThan(bold[0].FromMinute, "a cautious club protects a lead earliest");
        bold[2].FromMinute.Should().BeLessThan(balanced[2].FromMinute).And.BeLessThan(cautious[2].FromMinute, "a bold club chases earliest");
        cautious[0].Changes.MentalitySteps.Should().BeLessThan(balanced[0].Changes.MentalitySteps, "a cautious club steps further towards defence");
        bold[2].Changes.MentalitySteps.Should().BeGreaterThan(balanced[2].Changes.MentalitySteps, "a bold club steps further towards attack");
    }

    [Fact]
    public void Every_temperament_occurs_among_clubs()
    {
        var counts = Enumerable.Range(0, 600)
            .GroupBy(index => AiMatchPlanPolicy.Decide(ClubId(index), FormationPreset.FourFourTwo, Neutral).Temperament)
            .ToDictionary(group => group.Key, group => group.Count());

        counts.Keys.Should().BeEquivalentTo(Enum.GetValues<AiTemperament>());
        counts.Values.Should().OnlyContain(count => count > 140 && count < 260, "the draw is one in three");
    }

    [Theory]
    [InlineData(TimeWasting.Off, TimeWasting.Situational)]
    [InlineData(TimeWasting.Situational, null)]
    [InlineData(TimeWasting.On, null)]
    public void Protecting_a_lead_only_ever_raises_time_wasting(TimeWasting held, TimeWasting? expected)
    {
        var plan = AiMatchPlanPolicy.Decide(ClubId(2), FormationPreset.FourFourTwo, Neutral with { TimeWasting = held });

        plan.Instructions[0].Changes.TimeWasting.Should().Be(expected);
    }

    [Theory]
    [InlineData(FormationPreset.FourFourTwo, FormationPreset.FourFiveOne)]
    [InlineData(FormationPreset.ThreeFiveTwo, FormationPreset.FourFiveOne)]
    [InlineData(FormationPreset.FourThreeThree, FormationPreset.FourFourTwo)]
    [InlineData(FormationPreset.FiveThreeTwo, FormationPreset.FiveFourOne)]
    [InlineData(FormationPreset.FourTwoThreeOne, null)]
    [InlineData(FormationPreset.FourOneFourOne, null)]
    public void A_side_down_to_ten_drops_a_forward_and_a_one_forward_side_keeps_its_shape(FormationPreset formation, FormationPreset? expected)
    {
        AiMatchPlanPolicy.SettledShape(formation).Should().Be(expected);

        var plan = AiMatchPlanPolicy.Decide(ClubId(4), formation, Neutral);

        plan.Instructions[4].Formation.Should().Be(expected);

        if (expected is { } settled)
        {
            Forwards(settled).Should().Be(Forwards(formation) - 1, "the settled shape has one forward fewer");
        }
    }

    [Fact]
    public void The_plan_does_not_move_the_choices_the_policy_made_before_it_existed()
    {
        // The pin of the six clubs' shapes, instructions and elevens is in AiClubPolicyTests; this holds the other half: the plan rides on the
        // decision and is a pure function of what the decision already holds.
        var decision = AiClubPolicy.Decide(ClubId(5), Squad());
        var again = AiMatchPlanPolicy.Decide(ClubId(5), decision.Formation, decision.Instructions);

        decision.MatchPlan.Temperament.Should().Be(again.Temperament);
        decision.MatchPlan.Instructions.Should().BeEquivalentTo(again.Instructions, options => options.WithStrictOrdering());
    }

    [Fact]
    public void The_match_plan_has_its_own_seed_label()
    {
        AiPolicyVersions.MatchPlanVersion.Should().NotBe(AiPolicyVersions.Version, "the version is part of every other draw's seed, so sharing it would move them");
        AiPolicyVersions.Version.Should().Be("ai-policy-v1", "bumping it would give every club a new shape and new instructions");
    }

    private static int Forwards(FormationPreset formation) =>
        FormationLayouts.DefaultSlots(formation).Count(slot => slot.PositionFamily == PositionFamily.Attack);

    private static Guid ClubId(int index) => Guid.Parse($"0192f500-0000-7000-8000-{index:D12}");

    private static List<AiSquadPlayer> Squad()
    {
        PlayerPosition[] composition =
        [
            PlayerPosition.Goalkeeper,
            PlayerPosition.RightBack,
            PlayerPosition.CentreBack,
            PlayerPosition.CentreBack,
            PlayerPosition.LeftBack,
            PlayerPosition.DefensiveMidfielder,
            PlayerPosition.CentralMidfielder,
            PlayerPosition.CentralMidfielder,
            PlayerPosition.RightWinger,
            PlayerPosition.LeftWinger,
            PlayerPosition.Striker,
            PlayerPosition.Striker,
        ];

        return
        [
            .. composition.Select((position, index) => new AiSquadPlayer(
                Guid.Parse($"0192f600-0000-7000-8000-{index:D12}"),
                position,
                [],
                [.. Enumerable.Repeat(12, 28)],
                8_000,
                IsAvailable: true)),
        ];
    }
}

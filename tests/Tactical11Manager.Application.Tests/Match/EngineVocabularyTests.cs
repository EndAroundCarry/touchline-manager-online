using FluentAssertions;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.Application.Tests.Match;

/// <summary>
/// The mapping between the domain's vocabulary and the engine's (`MAT-1`, DEP-2).
/// </summary>
/// <remarks>
/// The two vocabularies are separate on purpose — the engine may not depend on the domain, and a played
/// match's reproducibility is a different contract from a generated world's — so the values are duplicated
/// and the application layer is the only place that knows both. These tests are what make the duplication
/// safe: every member is mapped deliberately, and the numbers on both sides agree, which is the property the
/// snapshot hash depends on.
/// </remarks>
public sealed class EngineVocabularyTests
{
    [Fact]
    public void Every_position_maps_to_the_engine_value_of_the_same_name()
    {
        foreach (var position in Enum.GetValues<PlayerPosition>())
        {
            ((int)EngineVocabulary.Position(position)).Should().Be(
                (int)position,
                $"{position} mirrors the engine's MatchPosition by value");
        }
    }

    [Fact]
    public void Every_family_maps_to_the_engine_value_of_the_same_name()
    {
        foreach (var family in Enum.GetValues<PositionFamily>())
        {
            ((int)EngineVocabulary.Family(family)).Should().Be((int)family);
        }
    }

    [Fact]
    public void Every_role_maps_to_the_engine_value_of_the_same_name()
    {
        foreach (var role in Enum.GetValues<PlayerRole>())
        {
            ((int)EngineVocabulary.Role(role)).Should().Be((int)role);
        }
    }

    [Fact]
    public void Every_event_type_maps_to_the_stored_value_of_the_same_name()
    {
        var stored = Enum.GetValues<Domain.Match.MatchEventType>();

        foreach (var type in Enum.GetValues<EngineEventType>())
        {
            var mapped = EngineVocabulary.EventType(type);

            ((int)mapped).Should().Be(
                (int)type,
                $"{type} is stored under a value that mirrors the engine's (MAT-8)");

            stored.Should().Contain(mapped);
        }

        stored.Should().HaveCount(Enum.GetValues<EngineEventType>().Length, "no event type is left unmapped");
    }

    [Fact]
    public void Every_shot_zone_and_substitution_reason_maps_by_value()
    {
        foreach (var zone in Enum.GetValues<ShotZone>())
        {
            ((int)EngineVocabulary.Zone(zone)).Should().Be((int)zone);
        }

        foreach (var reason in Enum.GetValues<MatchSubstitutionReason>())
        {
            ((int)EngineVocabulary.SubstitutionCause(reason)).Should().Be((int)reason);
        }
    }

    [Fact]
    public void The_instructions_map_by_value()
    {
        var instructions = new TeamInstructionSet
        {
            Mentality = Mentality.Attacking,
            Tempo = Tempo.High,
            Passing = PassingStyle.DirectPassing,
            Width = Width.Wide,
            PassFocus = PassFocus.CentreAndLeft,
            Pressing = Pressing.HighPress,
            DefensiveLine = DefensiveLine.High,
            Tackling = TacklingStyle.Aggressive,
            TimeWasting = TimeWasting.Situational,
            CounterAttack = true,
        };

        var mapped = EngineVocabulary.Instructions(instructions);

        ((int)mapped.Mentality).Should().Be((int)instructions.Mentality);
        ((int)mapped.Tempo).Should().Be((int)instructions.Tempo);
        ((int)mapped.Passing).Should().Be((int)instructions.Passing);
        ((int)mapped.Width).Should().Be((int)instructions.Width);
        ((int)mapped.PassFocus).Should().Be((int)instructions.PassFocus);
        ((int)mapped.Pressing).Should().Be((int)instructions.Pressing);
        ((int)mapped.DefensiveLine).Should().Be((int)instructions.DefensiveLine);
        ((int)mapped.Tackling).Should().Be((int)instructions.Tackling);
        ((int)mapped.TimeWasting).Should().Be((int)instructions.TimeWasting);
        mapped.CounterAttack.Should().BeTrue();
    }

    [Fact]
    public void The_counter_attack_is_off_unless_the_plan_asks_for_it()
    {
        EngineVocabulary.Instructions(TeamInstructionSet.Neutral).CounterAttack.Should().BeFalse();
    }

    [Fact]
    public void Every_pass_focus_maps_to_its_namesake()
    {
        foreach (var focus in Enum.GetValues<PassFocus>())
        {
            var mapped = EngineVocabulary.Instructions(TeamInstructionSet.Neutral with { PassFocus = focus });

            mapped.PassFocus.ToString().Should().Be(focus.ToString());
        }
    }

    [Fact]
    public void Every_match_instruction_word_maps_to_the_engine_value_of_the_same_name()
    {
        foreach (var value in Enum.GetValues<MatchInstructionScore>())
        {
            EngineVocabulary.RuleScore(value).ToString().Should().Be(value.ToString());
            ((int)EngineVocabulary.RuleScore(value)).Should().Be((int)value);
        }

        foreach (var value in Enum.GetValues<MatchInstructionEvent>())
        {
            EngineVocabulary.RuleEvent(value).ToString().Should().Be(value.ToString());
            ((int)EngineVocabulary.RuleEvent(value)).Should().Be((int)value);
        }

        foreach (var value in Enum.GetValues<MatchInstructionOff>())
        {
            EngineVocabulary.RuleOff(value).ToString().Should().Be(value.ToString());
            ((int)EngineVocabulary.RuleOff(value)).Should().Be((int)value);
        }

        foreach (var value in Enum.GetValues<MatchInstructionOn>())
        {
            EngineVocabulary.RuleOn(value).ToString().Should().Be(value.ToString());
            ((int)EngineVocabulary.RuleOn(value)).Should().Be((int)value);
        }

        foreach (var value in Enum.GetValues<Mentality>())
        {
            EngineVocabulary.MentalityOf(value).ToString().Should().Be(value.ToString());
            ((int)EngineVocabulary.MentalityOf(value)).Should().Be((int)value, "the opponent's mentality travels as a number");
        }
    }

    [Fact]
    public void A_match_instruction_carries_only_the_instructions_it_names()
    {
        EngineVocabulary.InstructionChanges(MatchInstructionChanges.None).Should().BeNull();

        var changes = EngineVocabulary.InstructionChanges(new MatchInstructionChanges
        {
            Tempo = Tempo.High,
            Tackling = TacklingStyle.StayOnFeet,
            PassFocus = PassFocus.Wings,
            CounterAttack = false,
            MentalitySteps = -2,
        })!;

        changes.Tempo.Should().Be(MatchTempo.High);
        changes.Tackling.Should().Be(MatchTacklingStyle.StayOnFeet);
        changes.PassFocus.Should().Be(MatchPassFocus.Wings);
        changes.CounterAttack.Should().BeFalse("turning the counter-attack off is a change");
        changes.MentalitySteps.Should().Be(-2);
        changes.Mentality.Should().BeNull();
        changes.Passing.Should().BeNull();
        changes.Width.Should().BeNull();
        changes.Pressing.Should().BeNull();
        changes.DefensiveLine.Should().BeNull();
        changes.TimeWasting.Should().BeNull();
    }

    [Fact]
    public void The_domains_shape_fit_grades_familiarity_with_the_engines_penalties()
    {
        // The domain may not reference the engine, so its copy of the shape fit holds the penalties as numbers of its own.
        var rules = MatchEngine.Configuration.EngineRulesV2.Default;

        FormationShapeFit.Certain.Should().Be(MatchEngine.Configuration.EngineRulesV2.Certain);
        FormationShapeFit.UnfamiliarRoleBasisPoints.Should().Be(rules.UnfamiliarRolePenaltyBasisPoints);
        FormationShapeFit.SecondaryPositionBasisPoints.Should().Be(rules.SecondaryPositionPenaltyBasisPoints);
        FormationShapeFit.OutOfPositionBasisPoints.Should().Be(rules.OutOfPositionPenaltyBasisPoints);
    }

    [Fact]
    public void The_domains_limits_are_the_engines()
    {
        MatchInstruction.MaxPerPlan.Should().Be(MatchPlanV1.MaxRules);
        MatchInstruction.MaxSubstitutions.Should().Be(MatchPlanV1.MaxSubstitutionsPerRule);
        MatchInstruction.MaxMinute.Should().Be(MatchRuleV1.MaxMinute);
        MatchInstruction.MaxScoreMargin.Should().Be(MatchRuleV1.MaxScoreMargin);
        MatchInstructionChanges.MaxMentalitySteps.Should().Be(MatchInstructionChangesV1.MaxMentalitySteps);
        MatchInstruction.MatchSubstitutionLimit.Should().Be(MatchEngine.Configuration.EngineRulesV2.Default.MaxSubstitutions);
    }

    [Fact]
    public void The_neutral_instruction_set_is_the_engines_own_middle()
    {
        // A club that has saved no plan takes the field with the set that neither instructs nor inhibits, and
        // the engine's default instructions are the same values.
        var neutral = EngineVocabulary.Instructions(TeamInstructionSet.Neutral);
        var engine = new MatchInstructionsV1();

        neutral.Should().Be(engine);
    }
}

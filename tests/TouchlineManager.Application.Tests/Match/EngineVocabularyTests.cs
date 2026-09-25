using FluentAssertions;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Tests.Match;

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
    public void The_eight_instructions_map_by_value()
    {
        var instructions = new TeamInstructionSet
        {
            Mentality = Mentality.Attacking,
            Tempo = Tempo.High,
            Passing = PassingStyle.DirectPassing,
            Width = Width.Wide,
            Pressing = Pressing.HighPress,
            DefensiveLine = DefensiveLine.High,
            Tackling = TacklingStyle.Aggressive,
            TimeWasting = TimeWasting.Situational,
        };

        var mapped = EngineVocabulary.Instructions(instructions);

        ((int)mapped.Mentality).Should().Be((int)instructions.Mentality);
        ((int)mapped.Tempo).Should().Be((int)instructions.Tempo);
        ((int)mapped.Passing).Should().Be((int)instructions.Passing);
        ((int)mapped.Width).Should().Be((int)instructions.Width);
        ((int)mapped.Pressing).Should().Be((int)instructions.Pressing);
        ((int)mapped.DefensiveLine).Should().Be((int)instructions.DefensiveLine);
        ((int)mapped.Tackling).Should().Be((int)instructions.Tackling);
        ((int)mapped.TimeWasting).Should().Be((int)instructions.TimeWasting);
    }

    [Fact]
    public void The_neutral_instruction_set_is_the_engines_own_middle()
    {
        // A club that has saved no plan takes the field with the set that neither instructs nor inhibits, and
        // the engine's default instructions are the same eight values.
        var neutral = EngineVocabulary.Instructions(TeamInstructionSet.Neutral);
        var engine = new MatchInstructionsV1();

        neutral.Should().Be(engine);
    }
}

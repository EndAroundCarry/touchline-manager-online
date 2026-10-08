using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the engine abstraction layer, engine registry, and non-destructive legacy wrapping (Milestone 0).
/// </summary>
public sealed class EngineAbstractionTests
{
    [Fact]
    public void Legacy_possession_engine_implements_interface_and_exposes_legacy_version_labels()
    {
        var legacy = LegacyPossessionEngine.Instance;

        legacy.Should().BeAssignableTo<IMatchSimulationEngine>();
        legacy.EngineLabel.Should().Be("engine-v11");
        legacy.RuleSetLabel.Should().Be("engine-rules-v10");
        legacy.EngineLabel.Should().Be(EngineVersions.LegacyEngineLabel);
        legacy.RuleSetLabel.Should().Be(EngineVersions.LegacyRuleSetLabel);
    }

    [Fact]
    public void Tick_match_engine_implements_interface_and_exposes_current_version_labels()
    {
        var tick = TickMatchEngine.Instance;

        tick.Should().BeAssignableTo<IMatchSimulationEngine>();
        tick.EngineLabel.Should().Be("engine-v12");
        tick.RuleSetLabel.Should().Be("engine-rules-v11");
        tick.EngineLabel.Should().Be(EngineVersions.EngineLabel);
        tick.RuleSetLabel.Should().Be(EngineVersions.RuleSetLabel);
    }

    [Fact]
    public void Engine_registry_resolves_engines_by_version_label()
    {
        MatchEngineRegistry.Resolve(EngineVersions.LegacyEngineLabel)
            .Should().BeSameAs(LegacyPossessionEngine.Instance);

        MatchEngineRegistry.Resolve(EngineVersions.EngineLabel)
            .Should().BeSameAs(TickMatchEngine.Instance);

        MatchEngineRegistry.DefaultEngine
            .Should().BeSameAs(TickMatchEngine.Instance);
    }

    [Fact]
    public void Match_simulator_simulates_legacy_v11_snapshot_through_legacy_engine()
    {
        var legacyInput = TestMatchFactory.Even(seed: 42UL);

        var result = MatchSimulator.Simulate(legacyInput);

        result.Should().NotBeNull();
        result.Events.Should().NotBeEmpty();
        result.TotalMinutesPlayed.Should().BeInRange(90, 105);
    }

    [Fact]
    public void Match_simulator_simulates_v12_snapshot_through_registered_tick_engine()
    {
        var v12Input = TestMatchFactory.Even(seed: 42UL) with
        {
            EngineVersion = EngineVersions.EngineLabel,
            RuleSetVersion = EngineVersions.RuleSetLabel,
            FormulaConfigurationHash = EngineConfiguration.HashOf(EngineRulesV2.Default, EngineVersions.RuleSetLabel),
        };

        var result = MatchSimulator.Simulate(v12Input);

        result.Should().NotBeNull();
        result.Events.Should().NotBeEmpty();
        result.TotalMinutesPlayed.Should().BeInRange(90, 105);
    }

    [Fact]
    public void Unknown_engine_version_is_rejected_with_named_exception()
    {
        var badInput = TestMatchFactory.Even() with
        {
            EngineVersion = "engine-v99",
        };

        var act = () => MatchSimulator.Simulate(badInput);

        act.Should().Throw<InvalidMatchInputException>()
            .WithMessage("*engine-v99*");
    }
}

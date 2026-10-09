using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Serialization;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine;

/// <summary>A played match and the film its replay plays (`tick-engine-v1`, Milestone 9).</summary>
/// <param name="Result">The result, including both hashes, exactly as <see cref="MatchSimulator.Simulate(MatchInputV1)"/> returns it.</param>
/// <param name="Presentation">The film the presentation carries, as the engine that played the match describes it.</param>
public sealed record MatchFilm(MatchResultV1 Result, MatchPresentationV1 Presentation);

/// <summary>
/// The match engine: a pure, deterministic simulation of one fixture from a frozen snapshot.
/// </summary>
/// <remarks>
/// <para>
/// The whole public surface of the engine is one method. Everything else — the ratings, the possession model,
/// the discipline, the substitutions — is reachable only through a snapshot, so there is no way to ask the
/// engine a question about live state, and no way for a caller to influence a result part-way through
/// (ADR-0004, `MAT-2`).
/// </para>
/// <para>
/// Nothing here reads a clock, a database, a culture, the network, or <see cref="Random"/>. The only
/// randomness is a <see cref="Pcg32"/> seeded from the snapshot, and the only time is the snapshot's own
/// clock. That is what makes a result reproducible byte for byte years after it was played.
/// </para>
/// </remarks>
public static class MatchSimulator
{
    /// <summary>Simulates a match under the current rules set.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <returns>The result, including both hashes.</returns>
    /// <exception cref="InvalidMatchInputException">When the snapshot cannot be simulated.</exception>
    public static MatchResultV1 Simulate(MatchInputV1 input) =>
        Simulate(input, EngineRulesV2.Default, liveMetrics: null);

    /// <summary>Simulates a match under an explicitly supplied rules set, capturing replay side channels.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="liveMetrics">
    /// The recorder the replay's minute-by-minute condition and ratings are captured into, or null when no
    /// replay is being derived. Capturing consumes no draw and changes no state, so the result is identical
    /// either way (`MAT-8`, §9.5).
    /// </param>
    /// <param name="passages">
    /// The recorder the replay's ball paths and touches are captured into, or null when no film is being
    /// derived (`engine-v4`). Like <paramref name="liveMetrics"/> it is a by-product of the same run: the
    /// geometry it captures is drawn from a per-possession stream of its own, so the output hash is identical
    /// with and without it — which is what the with/without-recorder test pins.
    /// </param>
    /// <returns>The result, including both hashes.</returns>
    /// <exception cref="InvalidMatchInputException">
    /// When the snapshot cannot be simulated, or was frozen against a different engine or rules version.
    /// </exception>
    public static MatchResultV1 Simulate(
        MatchInputV1 input,
        EngineRulesV2 rules,
        PlayerLiveMetricsRecorder? liveMetrics = null,
        MatchPassageRecorder? passages = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);

        input.Validate();
        rules.Validate();
        VerifyVersionAgreement(input, rules);

        var random = new Pcg32(input.Seed);

        var home = BuildSide(input.Home, MatchSide.Home, rules);
        var away = BuildSide(input.Away, MatchSide.Away, rules);

        var state = new MatchState(input, rules, random, home, away)
        {
            LiveMetrics = liveMetrics,
            Passages = passages,
        };

        MatchEngineRegistry.Resolve(input.EngineVersion).Run(state);

        return Complete(state, input);
    }

    /// <summary>Simulates a match and derives its film under the current rules set.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <returns>The result and the film, from one run of the engine the snapshot names.</returns>
    /// <exception cref="InvalidMatchInputException">When the snapshot cannot be simulated.</exception>
    public static MatchFilm SimulateFilm(MatchInputV1 input) =>
        SimulateFilm(input, EngineRulesV2.Default, liveMetrics: null);

    /// <summary>
    /// Simulates a match and derives the film its replay plays, in one pass (`tick-engine-v1`, Milestone 9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result the replay is verified against and the film it plays are produced by the same run, so the
    /// curve, the score and the movements belong to one match rather than two derivations that could drift.
    /// Which film is derived is the engine's business: a possession match records its passages and hands them
    /// to <see cref="ReplayDirector"/>, a tick match records its continuous trace and hands it to
    /// <see cref="TickReplaySynthesizer"/>.
    /// </para>
    /// <para>
    /// The tick film is retried at widening tolerances until it fits the presentation's payload budget, and a
    /// possession film does the same inside the director, so the same match always lands on the same rung
    /// (ADR-0006: deterministic compression).
    /// </para>
    /// </remarks>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="liveMetrics">
    /// The recorder the replay's minute-by-minute condition and ratings are captured into, or null when no
    /// curve is being derived.
    /// </param>
    /// <param name="options">How long a passage may run, how much is worth showing, and the film's pacing.</param>
    /// <returns>The result and the film, from one run of the engine the snapshot names.</returns>
    /// <exception cref="InvalidMatchInputException">
    /// When the snapshot cannot be simulated, or was frozen against a different engine or rules version.
    /// </exception>
    public static MatchFilm SimulateFilm(
        MatchInputV1 input,
        EngineRulesV2 rules,
        PlayerLiveMetricsRecorder? liveMetrics = null,
        HighlightOptionsV1? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);

        input.Validate();
        rules.Validate();
        VerifyVersionAgreement(input, rules);

        var random = new Pcg32(input.Seed);

        var home = BuildSide(input.Home, MatchSide.Home, rules);
        var away = BuildSide(input.Away, MatchSide.Away, rules);

        var passages = new MatchPassageRecorder();
        var state = new MatchState(input, rules, random, home, away)
        {
            LiveMetrics = liveMetrics,
            Passages = passages,
        };

        var engine = MatchEngineRegistry.Resolve(input.EngineVersion);
        var settings = options ?? new HighlightOptionsV1();

        if (engine is not TickMatchEngine)
        {
            engine.Run(state);

            var possessionResult = Complete(state, input);

            return new MatchFilm(
                possessionResult,
                ReplayDirector.Build(input, possessionResult, passages.Passages, settings, liveMetrics?.Metrics));
        }

        var recorder = new TickMatchRecorder();

        TickMatchLoop.Run(state, recorder);

        var result = Complete(state, input);

        return new MatchFilm(result, TickFilm(input, result, state, recorder.Build(), settings, liveMetrics));
    }

    /// <summary>Derives the tick match's film, retried at widening rungs until the presentation fits its budget.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="result">The finished result.</param>
    /// <param name="state">The match state, for the synthesizer's events, rules and colours.</param>
    /// <param name="recording">The continuous trace the run produced.</param>
    /// <param name="options">How long a passage may run, how much is worth showing, and the film's pacing.</param>
    /// <param name="liveMetrics">The recorder the curve was captured into, or null.</param>
    private static MatchPresentationV1 TickFilm(
        MatchInputV1 input,
        MatchResultV1 result,
        MatchState state,
        TickMatchRecording recording,
        HighlightOptionsV1 options,
        PlayerLiveMetricsRecorder? liveMetrics)
    {
        var presentation = TickReplayDirector.Build(
            input,
            result,
            TickReplaySynthesizer.Synthesize(state, recording, options),
            recording,
            state,
            options,
            liveMetrics?.Metrics);

        for (var rung = 1; rung < options.TickPlayerTolerances.Count && presentation.EstimatedPayloadBytes > options.PayloadBudgetBytes; rung++)
        {
            presentation = TickReplayDirector.Build(
                input,
                result,
                TickReplaySynthesizer.Synthesize(state, recording, options, rung),
                recording,
                state,
                options,
                liveMetrics?.Metrics);
        }

        return presentation;
    }

    /// <summary>Captures the final states and assembles the output contract from a finished run.</summary>
    /// <param name="state">The finished match state.</param>
    /// <param name="input">The frozen snapshot the result is bound to.</param>
    private static MatchResultV1 Complete(MatchState state, MatchInputV1 input)
    {
        state.Home.CaptureEndOfMatchStates();
        state.Away.CaptureEndOfMatchStates();

        return MatchResultBuilder.Build(state, CanonicalMatchSerializer.InputHash(input), state.Ball.GroundPoint);
    }

    /// <summary>
    /// Refuses to simulate a snapshot that was frozen against a different engine, rules version, or
    /// configuration.
    /// </summary>
    /// <remarks>
    /// The three checks are one idea: the snapshot says what produced it, and the engine either is that thing
    /// or declines. Simulating anyway would produce a plausible result whose stored hashes claim a
    /// provenance it does not have, which is worse than a refusal because it is not detectable later.
    /// </remarks>
    private static void VerifyVersionAgreement(MatchInputV1 input, EngineRulesV2 rules)
    {
        var isCurrentEngine = string.Equals(input.EngineVersion, EngineVersions.EngineLabel, StringComparison.Ordinal);
        var isLegacyEngine = string.Equals(input.EngineVersion, EngineVersions.LegacyEngineLabel, StringComparison.Ordinal);

        if (!isCurrentEngine && !isLegacyEngine)
        {
            throw new InvalidMatchInputException(
                $"The snapshot was frozen for engine '{input.EngineVersion}' but this is "
                + $"'{EngineVersions.EngineLabel}' (ADR-0004: a released engine version is never altered in place; legacy fallback: '{EngineVersions.LegacyEngineLabel}').");
        }

        var isCurrentRuleSet = string.Equals(input.RuleSetVersion, EngineVersions.RuleSetLabel, StringComparison.Ordinal);
        var isLegacyRuleSet = string.Equals(input.RuleSetVersion, EngineVersions.LegacyRuleSetLabel, StringComparison.Ordinal);

        if (!isCurrentRuleSet && !isLegacyRuleSet)
        {
            throw new InvalidMatchInputException(
                $"The snapshot was frozen for rules '{input.RuleSetVersion}' but this is "
                + $"'{EngineVersions.RuleSetLabel}' (legacy fallback: '{EngineVersions.LegacyRuleSetLabel}').");
        }

        var expected = EngineConfiguration.HashOf(rules, input.RuleSetVersion);

        if (!string.Equals(input.FormulaConfigurationHash, expected, StringComparison.Ordinal))
        {
            throw new InvalidMatchInputException(
                "The snapshot's formula configuration hash does not match the rules supplied, so one of them "
                + "is not the configuration this match was frozen against (MAT-9).");
        }
    }

    private static SideRuntime BuildSide(MatchSideV1 side, MatchSide which, EngineRulesV2 rules)
    {
        var lineup = LineupResolver.Resolve(side, which, rules);

        var runtime = new SideRuntime
        {
            Which = which,
            Lineup = lineup,
            Bench = [.. lineup.Bench],
        };

        foreach (var slot in lineup.Slots)
        {
            runtime.Active.Add(ActiveSlot.From(slot));

            // The eleven were on from kickoff, and their kickoff morale is what the scoreline's drift is
            // measured against.
            runtime.EnteredMinute[slot.Participant.ParticipantId] = 0;
            runtime.MoraleBaseline[slot.Participant.ParticipantId] = slot.Participant.State.MoraleBasisPoints;
        }

        runtime.RecalculateRatings(rules);

        return runtime;
    }
}

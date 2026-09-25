using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Serialization;
using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine;

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
    public static MatchResultV1 Simulate(MatchInputV1 input) => Simulate(input, EngineRulesV1.Default);

    /// <summary>Simulates a match under an explicitly supplied rules set.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="rules">The rules in force. Its hash must match the snapshot's.</param>
    /// <returns>The result, including both hashes.</returns>
    /// <exception cref="InvalidMatchInputException">
    /// When the snapshot cannot be simulated, or was frozen against a different engine or rules version.
    /// </exception>
    public static MatchResultV1 Simulate(MatchInputV1 input, EngineRulesV1 rules)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);

        input.Validate();
        rules.Validate();
        VerifyVersionAgreement(input, rules);

        var random = new Pcg32(input.Seed);

        var home = BuildSide(input.Home, MatchSide.Home, rules);
        var away = BuildSide(input.Away, MatchSide.Away, rules);

        var state = new MatchState(input, rules, random, home, away);

        PossessionSimulator.Run(state);

        return MatchResultBuilder.Build(state, CanonicalMatchSerializer.InputHash(input));
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
    private static void VerifyVersionAgreement(MatchInputV1 input, EngineRulesV1 rules)
    {
        if (!string.Equals(input.EngineVersion, EngineVersions.EngineLabel, StringComparison.Ordinal))
        {
            throw new InvalidMatchInputException(
                $"The snapshot was frozen for engine '{input.EngineVersion}' but this is "
                + $"'{EngineVersions.EngineLabel}' (ADR-0004: a released engine version is never altered in place).");
        }

        if (!string.Equals(input.RuleSetVersion, EngineVersions.RuleSetLabel, StringComparison.Ordinal))
        {
            throw new InvalidMatchInputException(
                $"The snapshot was frozen for rules '{input.RuleSetVersion}' but this is "
                + $"'{EngineVersions.RuleSetLabel}'.");
        }

        var expected = EngineConfiguration.HashOf(rules);

        if (!string.Equals(input.FormulaConfigurationHash, expected, StringComparison.Ordinal))
        {
            throw new InvalidMatchInputException(
                "The snapshot's formula configuration hash does not match the rules supplied, so one of them "
                + "is not the configuration this match was frozen against (MAT-9).");
        }
    }

    private static SideRuntime BuildSide(MatchSideV1 side, MatchSide which, EngineRulesV1 rules)
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

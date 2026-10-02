namespace TouchlineManager.MatchEngine;

/// <summary>
/// The engine's version stamps, the identity of the contract a result was produced under.
/// </summary>
/// <remarks>
/// A released engine version is never altered in place (ADR-0004, RULE-3). Changing any formula, draw
/// order, serialization, or event semantic produces a new <see cref="Match"/> version, a new rules
/// version, and new golden hashes; matches already played keep the version that produced them so a
/// historical scoreline stays re-derivable. The values are recorded on every match input snapshot
/// (MAT-1, RULE-2), so a result can always be explained by the rules that were in force.
/// </remarks>
public static class EngineVersions
{
    /// <summary>
    /// The engine version implemented by this assembly.
    /// </summary>
    /// <remarks>
    /// Bump on any change to formulas, draw order, canonical serialization, or event semantics. The
    /// golden output hashes are pinned per version, so the bump is what makes the change honest rather
    /// than a silent rewrite of history.
    /// </remarks>
    public const int Engine = 4;

    /// <summary>
    /// The engine rules version implemented by this assembly.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Engine"/> because a tuning change and a behavioural change are different
    /// kinds of change: a constant may move within a rules version only if it produces a new rules
    /// version, and either kind requires the engine version to be re-pinned.
    /// </remarks>
    public const int RuleSet = 4;

    /// <summary>The stable label for engine version 4, used in hashes and diagnostics.</summary>
    /// <remarks>
    /// Version 4 is the continuous passage model: a possession no longer teleports to an absolute random
    /// point in its own half, but is played along a real chain of touches that begins where the last one
    /// left the ball — or at a restart — progresses into the attacking third, and ends at an
    /// outcome-appropriate point, so event coordinates, shots, and direct free kicks are meaningful. The
    /// geometry is drawn from a per-possession stream of its own, so the outcome formulas and their
    /// distributions are comparable to version 3 while the ball finally moves like football. An optional
    /// passage recorder captures the film for the replay without touching the result.
    /// </remarks>
    public const string EngineLabel = "engine-v4";

    /// <summary>The stable label for engine rules version 4.</summary>
    public const string RuleSetLabel = "engine-rules-v4";

    /// <summary>The stable label for the unit-rating weight table, versioned with the engine.</summary>
    public const string RatingWeightsLabel = "engine-ratings-v1";
}

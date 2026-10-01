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
    public const int Engine = 3;

    /// <summary>
    /// The engine rules version implemented by this assembly.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Engine"/> because a tuning change and a behavioural change are different
    /// kinds of change: a constant may move within a rules version only if it produces a new rules
    /// version, and either kind requires the engine version to be re-pinned.
    /// </remarks>
    public const int RuleSet = 3;

    /// <summary>The stable label for engine version 3, used in hashes and diagnostics.</summary>
    /// <remarks>
    /// Version 3 is the spatial play model (master plan Stage 2): every possession is located on the
    /// normalized 2D pitch, possessions resolve through explicit 1v1 duels and loose-ball scrambles, set
    /// pieces gain direct free kicks alongside corners and penalties, and the result carries each
    /// player's final condition and substitution minutes for the match center. The new duel, scramble,
    /// free-kick, and spatial constants are rules constants, so this is a new engine version and a new
    /// rules version, and the golden hashes are re-pinned against it.
    /// </remarks>
    public const string EngineLabel = "engine-v3";

    /// <summary>The stable label for engine rules version 3.</summary>
    public const string RuleSetLabel = "engine-rules-v3";

    /// <summary>The stable label for the unit-rating weight table, versioned with the engine.</summary>
    public const string RatingWeightsLabel = "engine-ratings-v1";
}

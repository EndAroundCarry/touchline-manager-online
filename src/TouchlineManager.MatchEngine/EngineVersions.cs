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
    public const int Engine = 8;

    /// <summary>
    /// The engine rules version implemented by this assembly.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Engine"/> because a tuning change and a behavioural change are different
    /// kinds of change: a constant may move within a rules version only if it produces a new rules
    /// version, and either kind requires the engine version to be re-pinned.
    /// </remarks>
    public const int RuleSet = 7;

    /// <summary>The stable label for engine version 8, used in hashes and diagnostics.</summary>
    /// <remarks>
    /// <para>
    /// Version 8 lets a manager direct the ball. A team instruction, the pass focus, asks for the centre alone,
    /// or the centre and one flank, and the lateral position of each possession's pressure point follows it, so
    /// the ball's approach is steered through those lanes. The draw that places the point is the one the engine
    /// always took, so a side with no preference plays exactly as it did under `engine-v7`; only the canonical
    /// serialization, the rules, and the hashes change for it.
    /// </para>
    /// <para>
    /// Version 7 counts what a player does with the ball. Each player's line carries the passes they attempted
    /// and completed and the take-ons they attempted and won, beside the goals and assists it already held. No
    /// play draw moves: the passes are credited from a stream derived from the seed and the possession, and
    /// the take-ons are the 1v1 duels the engine already resolved, so every scoreline, event, and passage is
    /// the one `engine-v6` produced and only the player lines, the canonical serialization, and the hashes
    /// change.
    /// </para>
    /// <para>
    /// Version 6 puts the engine's skills where its design said they were. Shots, saves, penalties, and free
    /// kicks compare a skill with the goalkeeper on the scale they are measured on, so finishing and
    /// goalkeeping now count at the moment of the shot. A tired player plays below his sheet, physical skills
    /// first, and both the team ratings and every duel read that effective skill; Stamina sets how fast a
    /// player tires, and ratings are refreshed every minute. Duels read position fit and playing short, and
    /// pick their players by band. A lost final-third duel can end in a foul, and a side's Aggression and
    /// Tackling set how often it fouls. Corners have a taker, penalties depend on taker and keeper, Leadership
    /// steadies morale, and time wasting follows the score and lengthens possessions. The Set pieces, Fitness,
    /// and Cohesion ratings, which nothing read, are gone, and the duel and shot-zone constants moved into the
    /// rules.
    /// </para>
    /// <para>
    /// Version 5 completed the continuous passage model that version 4 introduced. The clock is reset at
    /// half-time, so the second half is played from 45:00 with its own stoppage rather than starting a few
    /// minutes late and finishing a few minutes early. A dead ball belongs to somebody: a kick-off, a goal
    /// kick, a keeper's ball, a free kick, or an offside is taken by the side the rules give it to, from where
    /// the rules put it, and is consumed by the very next possession instead of being drawn afresh or left
    /// pending for a later one (`MAT-12`). The ball ends where play actually continues.
    /// </para>
    /// <para>
    /// The passage recorder gained the facts a film needs — the half, how the possession ended, the restart it
    /// began with, where each event sits, and real start and end times — and a shot now travels to a target
    /// the outcome decides: the goal mouth, the goalkeeper, wide or over, the woodwork, or a block. Everything
    /// the recorder captures is drawn from the possession's own geometry stream and never moves a play draw.
    /// </para>
    /// </remarks>
    public const string EngineLabel = "engine-v8";

    /// <summary>The stable label for engine rules version 7.</summary>
    public const string RuleSetLabel = "engine-rules-v7";

    /// <summary>The stable label for the unit-rating weight table, versioned with the engine.</summary>
    public const string RatingWeightsLabel = "engine-ratings-v2";
}

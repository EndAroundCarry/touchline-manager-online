namespace Tactical11Manager.Contracts.Squad;

/// <summary>
/// One match instruction of a tactical plan: a rule the engine carries out during a match when its conditions hold (`MAT-6`).
/// </summary>
/// <remarks>
/// <para>
/// The same shape goes up in a save and comes back in a read, so the two cannot drift. An instruction's place in the plan's list is its
/// place in the array, counted from 1; the engine checks the instructions in that order, and each fires at most once a match.
/// </para>
/// <para>
/// All codes are the stable codes of the domain's enums. A client changing the kind of event clears the family and threshold the old kind
/// used: the server refuses an event with a parameter it does not take.
/// </para>
/// </remarks>
public sealed record MatchInstructionModel
{
    /// <summary>Gets the manager-facing name, at most 48 characters.</summary>
    public required string Name { get; init; }

    /// <summary>Gets whether the instruction is on. One that is off is kept but never carried out.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets the first minute the instruction may fire in, 0–90.</summary>
    public int FromMinute { get; init; }

    /// <summary>Gets the last minute the instruction may fire in, or null for the rest of the match.</summary>
    public int? UntilMinute { get; init; }

    /// <summary>Gets the score condition code: <c>any</c>, <c>leading</c>, <c>level</c>, <c>trailing</c>, <c>not_winning</c> or <c>not_losing</c>.</summary>
    public string Score { get; init; } = "any";

    /// <summary>Gets the goal margin for <c>leading</c> and <c>trailing</c>, 1–5.</summary>
    public int ScoreMargin { get; init; } = 1;

    /// <summary>
    /// Gets the event code: <c>none</c>, <c>own_sent_off</c>, <c>opponent_sent_off</c>, <c>own_booked</c>, <c>playing_badly</c>, <c>tired</c>,
    /// <c>opponent_mentality_at_least</c> or <c>opponent_mentality_at_most</c>.
    /// </summary>
    public string Event { get; init; } = "none";

    /// <summary>Gets the position family code the event is narrowed to, or null for any outfield player.</summary>
    public string? EventFamily { get; init; }

    /// <summary>
    /// Gets the event's threshold: a live rating (basis points) for <c>playing_badly</c>, a condition (basis points) for <c>tired</c>, or a
    /// mentality number (0 defensive … 4 attacking) for the opponent's mentality.
    /// </summary>
    public int? EventThreshold { get; init; }

    /// <summary>Gets the formation preset code the instruction moves to, or null when it keeps the shape.</summary>
    public string? Formation { get; init; }

    /// <summary>Gets the manager's own placements in the new shape; the server fits everybody else.</summary>
    public IReadOnlyList<MatchInstructionOverrideModel> Overrides { get; init; } = [];

    /// <summary>Gets the team instructions the instruction changes.</summary>
    public MatchInstructionChangesModel Changes { get; init; } = new();

    /// <summary>Gets the substitutions the instruction makes, at most 5.</summary>
    public IReadOnlyList<MatchInstructionSubstitutionModel> Substitutions { get; init; } = [];
}

/// <summary>A manager's placement of one player in an instruction's new shape.</summary>
public sealed record MatchInstructionOverrideModel
{
    /// <summary>Gets the player, who must be in the plan's lineup.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>Gets the slot of the new shape he takes, 2–11.</summary>
    public required int SlotNumber { get; init; }
}

/// <summary>
/// The team instructions a match instruction changes: each is a code, or null for no change.
/// </summary>
public sealed record MatchInstructionChangesModel
{
    /// <summary>Gets the mentality code to adopt, or null.</summary>
    public string? Mentality { get; init; }

    /// <summary>Gets how many steps to move the mentality after it is set: positive more attacking, negative more defensive, −2…2.</summary>
    public int MentalitySteps { get; init; }

    /// <summary>Gets the tempo code to adopt, or null.</summary>
    public string? Tempo { get; init; }

    /// <summary>Gets the passing code to adopt, or null.</summary>
    public string? Passing { get; init; }

    /// <summary>Gets the width code to adopt, or null.</summary>
    public string? Width { get; init; }

    /// <summary>Gets the pass-focus code to adopt, or null.</summary>
    public string? PassFocus { get; init; }

    /// <summary>Gets the pressing code to adopt, or null.</summary>
    public string? Pressing { get; init; }

    /// <summary>Gets the defensive-line code to adopt, or null.</summary>
    public string? DefensiveLine { get; init; }

    /// <summary>Gets the tackling code to adopt, or null.</summary>
    public string? Tackling { get; init; }

    /// <summary>Gets the time-wasting code to adopt, or null.</summary>
    public string? TimeWasting { get; init; }

    /// <summary>Gets whether to switch the counter-attack on or off, or null for no change.</summary>
    public bool? CounterAttack { get; init; }
}

/// <summary>One substitution a match instruction makes.</summary>
public sealed record MatchInstructionSubstitutionModel
{
    /// <summary>Gets how the player coming off is chosen: <c>player</c>, <c>subject</c>, <c>most_tired_in_family</c> or <c>lowest_rated_in_family</c>.</summary>
    public string Off { get; init; } = "player";

    /// <summary>Gets the player coming off, for <c>player</c>.</summary>
    public Guid? OffPlayerId { get; init; }

    /// <summary>Gets the position family code the player coming off is chosen from, for the family selectors.</summary>
    public string? OffFamily { get; init; }

    /// <summary>Gets how the player coming on is chosen: <c>player</c> or <c>best_fit_in_family</c>.</summary>
    public string On { get; init; } = "player";

    /// <summary>Gets the player coming on, for <c>player</c>.</summary>
    public Guid? OnPlayerId { get; init; }

    /// <summary>Gets the position family code the player coming on is chosen from, for <c>best_fit_in_family</c>.</summary>
    public string? OnFamily { get; init; }
}

/// <summary>Something about a plan's match instructions that deserves a manager's attention but does not stop a save.</summary>
/// <param name="Code">The stable code: <c>INSTRUCTION_DOES_NOTHING</c>, <c>INSTRUCTION_OUT_OF_POSITION</c> or <c>INSTRUCTION_PLACED_PLAYER_LEAVES</c>.</param>
/// <param name="Ordinal">The instruction's place in the list, from 1.</param>
/// <param name="PlayerId">The player the warning concerns, when it concerns one.</param>
public sealed record MatchInstructionWarningResponse(string Code, int Ordinal, Guid? PlayerId = null);

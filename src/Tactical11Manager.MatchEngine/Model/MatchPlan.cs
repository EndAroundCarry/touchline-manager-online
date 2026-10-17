namespace Tactical11Manager.MatchEngine.Model;

/// <summary>What a match rule asks of the scoreline (`match-instructions-v1`).</summary>
public enum MatchRuleScore
{
    /// <summary>Any score.</summary>
    Any = 0,

    /// <summary>The side leads by at least the rule's margin.</summary>
    Leading = 1,

    /// <summary>The score is level.</summary>
    Level = 2,

    /// <summary>The side trails by at least the rule's margin.</summary>
    Trailing = 3,

    /// <summary>The side is not ahead: level or behind.</summary>
    NotWinning = 4,

    /// <summary>The side is not behind: level or ahead.</summary>
    NotLosing = 5,
}

/// <summary>The event a match rule waits for, besides the minute and the score (`match-instructions-v1`).</summary>
public enum MatchRuleEvent
{
    /// <summary>No event: the minute and the score are all the rule asks.</summary>
    None = 0,

    /// <summary>One of the side's players has been sent off.</summary>
    OwnSentOff = 1,

    /// <summary>One of the opponent's players has been sent off.</summary>
    OpponentSentOff = 2,

    /// <summary>One of the side's players on the pitch has been booked (the family narrows it). He is the rule's subject.</summary>
    OwnBooked = 3,

    /// <summary>
    /// One of the side's players is playing badly: his live rating is below the threshold, or, with no threshold, he is the lowest
    /// rated of the family. He is the rule's subject.
    /// </summary>
    PlayingBadly = 4,

    /// <summary>One of the side's players has a condition below the threshold (the family narrows it). He is the rule's subject.</summary>
    Tired = 5,

    /// <summary>The opponent's mentality is at least the threshold (a <see cref="MatchMentality"/> value): it chases the game.</summary>
    OpponentMentalityAtLeast = 6,

    /// <summary>The opponent's mentality is at most the threshold (a <see cref="MatchMentality"/> value): it parks the bus.</summary>
    OpponentMentalityAtMost = 7,
}

/// <summary>Which player a match rule's substitution takes off (`match-instructions-v1`).</summary>
public enum MatchRuleOff
{
    /// <summary>A named player, who is skipped when he has left the pitch.</summary>
    Player = 0,

    /// <summary>The player the rule's event is about: the booked, the tired or the badly playing one.</summary>
    Subject = 1,

    /// <summary>The most tired player on the pitch in a family.</summary>
    MostTiredInFamily = 2,

    /// <summary>The lowest rated player on the pitch in a family.</summary>
    LowestRatedInFamily = 3,
}

/// <summary>Which player a match rule's substitution brings on (`match-instructions-v1`).</summary>
public enum MatchRuleOn
{
    /// <summary>A named substitute, who is skipped when he is not on the bench.</summary>
    Player = 0,

    /// <summary>The substitute of a family who fits the vacated slot best and is freshest.</summary>
    BestFitInFamily = 1,
}

/// <summary>One substitution a match rule makes.</summary>
public sealed record MatchRuleSubstitutionV1
{
    /// <summary>Gets how the player coming off is chosen.</summary>
    public MatchRuleOff Off { get; init; }

    /// <summary>Gets the player coming off, for <see cref="MatchRuleOff.Player"/>.</summary>
    public Guid? OffParticipantId { get; init; }

    /// <summary>Gets the family the player coming off is chosen from, for the family selectors.</summary>
    public MatchPositionFamily? OffFamily { get; init; }

    /// <summary>Gets how the player coming on is chosen.</summary>
    public MatchRuleOn On { get; init; }

    /// <summary>Gets the player coming on, for <see cref="MatchRuleOn.Player"/>.</summary>
    public Guid? OnParticipantId { get; init; }

    /// <summary>Gets the family the player coming on is chosen from, for <see cref="MatchRuleOn.BestFitInFamily"/>.</summary>
    public MatchPositionFamily? OnFamily { get; init; }
}

/// <summary>A manager's own placement of a player in a rule's new shape.</summary>
/// <param name="ParticipantId">The player.</param>
/// <param name="SlotNumber">The slot of the new shape he takes.</param>
public sealed record MatchSlotOverrideV1(Guid ParticipantId, int SlotNumber);

/// <summary>
/// The instructions a match rule changes: each one is "no change" (null) or a value, and the mentality may also move by steps.
/// </summary>
public sealed record MatchInstructionChangesV1
{
    /// <summary>The most steps a rule may move the mentality by, either way.</summary>
    public const int MaxMentalitySteps = 2;

    /// <summary>Gets the mentality to adopt, or null for no change.</summary>
    public MatchMentality? Mentality { get; init; }

    /// <summary>
    /// Gets how many steps to move the mentality after <see cref="Mentality"/> is applied: positive is more attacking, negative more
    /// defensive, and it stops at <see cref="MatchMentality.Defensive"/> and <see cref="MatchMentality.Attacking"/>.
    /// </summary>
    public int MentalitySteps { get; init; }

    /// <summary>Gets the tempo to adopt, or null for no change.</summary>
    public MatchTempo? Tempo { get; init; }

    /// <summary>Gets the passing style to adopt, or null for no change.</summary>
    public MatchPassingStyle? Passing { get; init; }

    /// <summary>Gets the width to adopt, or null for no change.</summary>
    public MatchWidth? Width { get; init; }

    /// <summary>Gets the pass focus to adopt, or null for no change.</summary>
    public MatchPassFocus? PassFocus { get; init; }

    /// <summary>Gets the pressing to adopt, or null for no change.</summary>
    public MatchPressing? Pressing { get; init; }

    /// <summary>Gets the defensive line to adopt, or null for no change.</summary>
    public MatchDefensiveLine? DefensiveLine { get; init; }

    /// <summary>Gets the tackling to adopt, or null for no change.</summary>
    public MatchTacklingStyle? Tackling { get; init; }

    /// <summary>Gets the time-wasting setting to adopt, or null for no change.</summary>
    public MatchTimeWasting? TimeWasting { get; init; }

    /// <summary>Gets whether to play on the counter-attack, or null for no change.</summary>
    public bool? CounterAttack { get; init; }

    /// <summary>Gets whether the rule changes no instruction.</summary>
    public bool IsEmpty =>
        Mentality is null && MentalitySteps == 0 && Tempo is null && Passing is null && Width is null && PassFocus is null
        && Pressing is null && DefensiveLine is null && Tackling is null && TimeWasting is null && CounterAttack is null;

    /// <summary>Applies the changes to a set of instructions.</summary>
    /// <param name="current">The instructions in force.</param>
    /// <returns>The new instructions.</returns>
    public MatchInstructionsV1 ApplyTo(MatchInstructionsV1 current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var mentality = (int)(Mentality ?? current.Mentality) + MentalitySteps;

        return current with
        {
            Mentality = (MatchMentality)int.Clamp(mentality, (int)MatchMentality.Defensive, (int)MatchMentality.Attacking),
            Tempo = Tempo ?? current.Tempo,
            Passing = Passing ?? current.Passing,
            Width = Width ?? current.Width,
            PassFocus = PassFocus ?? current.PassFocus,
            Pressing = Pressing ?? current.Pressing,
            DefensiveLine = DefensiveLine ?? current.DefensiveLine,
            Tackling = Tackling ?? current.Tackling,
            TimeWasting = TimeWasting ?? current.TimeWasting,
            CounterAttack = CounterAttack ?? current.CounterAttack,
        };
    }
}

/// <summary>
/// One match instruction: a rule the engine carries out when its conditions hold, once, at the next stoppage (`match-instructions-v1`).
/// </summary>
/// <remarks>
/// <para>
/// All of the conditions must hold. The rule is checked at every stoppage and at half-time, in the order the plan lists its rules, and fires
/// at most once a match. What it does is, in this order: its substitutions, then its new shape, then its instruction changes.
/// </para>
/// <para>
/// The shape arrives as eleven slots the caller has already resolved from a formation, because the engine knows no formation presets.
/// </para>
/// </remarks>
public sealed record MatchRuleV1
{
    /// <summary>The most minutes a match shows on the clock: the rule's minutes run 0…90.</summary>
    public const int MaxMinute = 90;

    /// <summary>The largest goal margin a rule may ask for.</summary>
    public const int MaxScoreMargin = 5;

    /// <summary>Gets the rule's place in the manager's list, from 1: the tactical-change event carries it.</summary>
    public required int Ordinal { get; init; }

    /// <summary>
    /// Gets the name the manager gave the rule, or null. For presentation only: it is not part of the canonical text, so
    /// renaming a rule can never change what a match does or its hash. The match centre shows it to the rule's own manager.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>Gets the first minute the rule may fire in (the displayed minute, 0…90).</summary>
    public int FromMinute { get; init; }

    /// <summary>Gets the last minute the rule may fire in, or null for the rest of the match.</summary>
    public int? UntilMinute { get; init; }

    /// <summary>Gets what the rule asks of the score.</summary>
    public MatchRuleScore Score { get; init; }

    /// <summary>Gets the goal margin for <see cref="MatchRuleScore.Leading"/> and <see cref="MatchRuleScore.Trailing"/>, 1…5.</summary>
    public int ScoreMargin { get; init; } = 1;

    /// <summary>Gets the event the rule waits for.</summary>
    public MatchRuleEvent Event { get; init; }

    /// <summary>Gets the family the event is narrowed to, or null for any outfield player.</summary>
    public MatchPositionFamily? EventFamily { get; init; }

    /// <summary>
    /// Gets the event's threshold: a live rating in basis points for <see cref="MatchRuleEvent.PlayingBadly"/> (null with a family
    /// names the lowest rated), a condition in basis points for <see cref="MatchRuleEvent.Tired"/>, or a mentality for the opponent's.
    /// </summary>
    public int? EventThreshold { get; init; }

    /// <summary>Gets the code of the formation the rule moves to (for the event's text), or null when it keeps the shape.</summary>
    public string? FormationCode { get; init; }

    /// <summary>Gets the eleven slots of the new shape, or null when the rule keeps the shape. Slot 1 is the goalkeeper's.</summary>
    public IReadOnlyList<MatchSlotV1>? Slots { get; init; }

    /// <summary>Gets the manager's own placements in the new shape, which are made before the automatic fit.</summary>
    public IReadOnlyList<MatchSlotOverrideV1> Overrides { get; init; } = [];

    /// <summary>Gets the instructions the rule changes, or null for none.</summary>
    public MatchInstructionChangesV1? Instructions { get; init; }

    /// <summary>Gets the substitutions the rule makes, at most <see cref="MatchPlanV1.MaxSubstitutionsPerRule"/>.</summary>
    public IReadOnlyList<MatchRuleSubstitutionV1> Substitutions { get; init; } = [];
}

/// <summary>
/// What a side does about the match as it unfolds: its match instructions, and whether the assistant still makes fatigue substitutions
/// (`match-instructions-v1`).
/// </summary>
/// <remarks>
/// A side with no rules and the assistant on is <see cref="None"/>, which is every side of every match played before the feature, and
/// plays exactly as it did.
/// </remarks>
public sealed record MatchPlanV1
{
    /// <summary>The most rules a plan may hold.</summary>
    public const int MaxRules = 10;

    /// <summary>The most substitutions one rule may make.</summary>
    public const int MaxSubstitutionsPerRule = 3;

    /// <summary>Gets a plan with no rules and the assistant on.</summary>
    public static MatchPlanV1 None { get; } = new();

    /// <summary>Gets the rules, in the order they are checked.</summary>
    public IReadOnlyList<MatchRuleV1> Rules { get; init; } = [];

    /// <summary>Gets whether the assistant makes the fatigue substitutions at the usual windows.</summary>
    public bool AssistantFatigueSubstitutions { get; init; } = true;

    /// <summary>Gets whether the plan says anything: it has rules, or the assistant is off.</summary>
    public bool HasContent => Rules.Count > 0 || !AssistantFatigueSubstitutions;
}

using System.Text.Json;

namespace Tactical11Manager.Domain.Squad;

/// <summary>A manager's own placement of a player in an instruction's new shape.</summary>
/// <param name="PlayerId">The player, who must be in the plan's lineup.</param>
/// <param name="SlotNumber">The slot of the new shape he takes, 2–11 (slot 1 is the goalkeeper's).</param>
public sealed record MatchInstructionOverride(Guid PlayerId, int SlotNumber);

/// <summary>One substitution a match instruction makes.</summary>
/// <param name="Off">How the player coming off is chosen.</param>
/// <param name="OffPlayerId">The player coming off, for <see cref="MatchInstructionOff.Player"/>.</param>
/// <param name="OffFamily">The family the player coming off is chosen from, for the family selectors.</param>
/// <param name="On">How the player coming on is chosen.</param>
/// <param name="OnPlayerId">The player coming on, for <see cref="MatchInstructionOn.Player"/>.</param>
/// <param name="OnFamily">The family the player coming on is chosen from, for <see cref="MatchInstructionOn.BestFitInFamily"/>.</param>
public sealed record MatchInstructionSubstitution(
    MatchInstructionOff Off,
    Guid? OffPlayerId,
    PositionFamily? OffFamily,
    MatchInstructionOn On,
    Guid? OnPlayerId,
    PositionFamily? OnFamily);

/// <summary>
/// The team instructions a match instruction changes: each one is "no change" (null) or a value, and the mentality may also move by steps.
/// </summary>
public sealed record MatchInstructionChanges
{
    /// <summary>The most steps an instruction may move the mentality by, either way.</summary>
    public const int MaxMentalitySteps = 2;

    /// <summary>Gets no change at all.</summary>
    public static MatchInstructionChanges None { get; } = new();

    /// <summary>Gets the mentality to adopt, or null for no change.</summary>
    public Mentality? Mentality { get; init; }

    /// <summary>Gets how many steps to move the mentality after <see cref="Mentality"/> is applied: positive is more attacking, negative more defensive.</summary>
    public int MentalitySteps { get; init; }

    /// <summary>Gets the tempo to adopt, or null for no change.</summary>
    public Tempo? Tempo { get; init; }

    /// <summary>Gets the passing style to adopt, or null for no change.</summary>
    public PassingStyle? Passing { get; init; }

    /// <summary>Gets the width to adopt, or null for no change.</summary>
    public Width? Width { get; init; }

    /// <summary>Gets the pass focus to adopt, or null for no change.</summary>
    public PassFocus? PassFocus { get; init; }

    /// <summary>Gets the pressing to adopt, or null for no change.</summary>
    public Pressing? Pressing { get; init; }

    /// <summary>Gets the defensive line to adopt, or null for no change.</summary>
    public DefensiveLine? DefensiveLine { get; init; }

    /// <summary>Gets the tackling to adopt, or null for no change.</summary>
    public TacklingStyle? Tackling { get; init; }

    /// <summary>Gets the time-wasting setting to adopt, or null for no change.</summary>
    public TimeWasting? TimeWasting { get; init; }

    /// <summary>Gets whether to play on the counter-attack, or null for no change.</summary>
    public bool? CounterAttack { get; init; }

    /// <summary>Gets whether nothing changes.</summary>
    public bool IsEmpty =>
        Mentality is null && MentalitySteps == 0 && Tempo is null && Passing is null && Width is null && PassFocus is null
        && Pressing is null && DefensiveLine is null && Tackling is null && TimeWasting is null && CounterAttack is null;
}

/// <summary>
/// A match instruction as it is submitted for validation, before it becomes a <see cref="MatchInstruction"/>.
/// </summary>
/// <remarks>
/// A plain value, like <see cref="TacticalSlotDefinition"/>: a request is validated before anything is constructed, so a bad minute is a
/// reported issue and not an exception that surfaces as a 500. The instruction's place in the list is its position in the list, from 1.
/// </remarks>
public sealed record MatchInstructionDefinition
{
    /// <summary>Gets the manager-facing name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets whether the instruction is on. An instruction that is off is kept but never carried out.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets the first minute the instruction may fire in (the displayed minute, 0…90).</summary>
    public int FromMinute { get; init; }

    /// <summary>Gets the last minute the instruction may fire in, or null for the rest of the match.</summary>
    public int? UntilMinute { get; init; }

    /// <summary>Gets what the instruction asks of the score.</summary>
    public MatchInstructionScore Score { get; init; }

    /// <summary>Gets the goal margin for <see cref="MatchInstructionScore.Leading"/> and <see cref="MatchInstructionScore.Trailing"/>, 1…5.</summary>
    public int ScoreMargin { get; init; } = 1;

    /// <summary>Gets the event the instruction waits for.</summary>
    public MatchInstructionEvent Event { get; init; }

    /// <summary>Gets the family the event is narrowed to, or null for any outfield player.</summary>
    public PositionFamily? EventFamily { get; init; }

    /// <summary>
    /// Gets the event's threshold: a live rating in basis points for <see cref="MatchInstructionEvent.PlayingBadly"/> (null with a family
    /// names the lowest rated), a condition in basis points for <see cref="MatchInstructionEvent.Tired"/>, or a <see cref="Squad.Mentality"/>
    /// value for the opponent's mentality.
    /// </summary>
    public int? EventThreshold { get; init; }

    /// <summary>Gets the formation the instruction moves to, or null when it keeps the shape.</summary>
    public FormationPreset? Formation { get; init; }

    /// <summary>Gets the manager's own placements in the new shape, which are made before the automatic fit.</summary>
    public IReadOnlyList<MatchInstructionOverride> Overrides { get; init; } = [];

    /// <summary>Gets the team instructions the instruction changes.</summary>
    public MatchInstructionChanges Changes { get; init; } = MatchInstructionChanges.None;

    /// <summary>Gets the substitutions the instruction makes.</summary>
    public IReadOnlyList<MatchInstructionSubstitution> Substitutions { get; init; } = [];
}

/// <summary>
/// One match instruction of a tactical plan: a rule the engine carries out when its conditions hold during the match (`MAT-6`).
/// </summary>
/// <remarks>
/// <para>
/// All of the conditions must hold. The rule is checked at every stoppage and at half-time, in the order the plan lists its
/// instructions, and fires at most once a match. Its overrides and substitutions are stored as <c>jsonb</c> because they are lists of
/// small values that are only ever read and written with the instruction; the conditions and the instruction changes are columns, so
/// they can be checked and queried.
/// </para>
/// <para>
/// An instruction has no version of its own that anyone reads: the plan's version is the entity tag a team sheet pins and a save
/// checks, and every save of the instructions revises the plan.
/// </para>
/// </remarks>
public sealed class MatchInstruction
{
    /// <summary>The longest instruction name, so a column can be sized to hold one.</summary>
    public const int MaxNameLength = 48;

    /// <summary>The most instructions a plan may hold.</summary>
    public const int MaxPerPlan = 10;

    /// <summary>The most substitutions one instruction may make.</summary>
    public const int MaxSubstitutions = 5;

    /// <summary>The substitutions a club may make in a match (`SQ-5`): more across a plan's instructions may be skipped.</summary>
    public const int MatchSubstitutionLimit = 5;

    /// <summary>The latest minute an instruction may name: the clock runs 0…90.</summary>
    public const int MaxMinute = 90;

    /// <summary>The largest goal margin an instruction may ask for.</summary>
    public const int MaxScoreMargin = 5;

    /// <summary>The largest condition or rating threshold, in basis points.</summary>
    public const int MaxThreshold = 10_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private MatchInstruction()
    {
    }

    /// <summary>Gets the instruction identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning plan.</summary>
    public Guid PlanId { get; private set; }

    /// <summary>Gets the instruction's place in the plan's list, from 1. Instructions are checked in this order.</summary>
    public int Ordinal { get; private set; }

    /// <summary>Gets the manager-facing name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets whether the instruction is on.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Gets the first minute the instruction may fire in.</summary>
    public int FromMinute { get; private set; }

    /// <summary>Gets the last minute the instruction may fire in, or null for the rest of the match.</summary>
    public int? UntilMinute { get; private set; }

    /// <summary>Gets what the instruction asks of the score.</summary>
    public MatchInstructionScore Score { get; private set; }

    /// <summary>Gets the goal margin for the leading and trailing conditions.</summary>
    public int ScoreMargin { get; private set; }

    /// <summary>Gets the event the instruction waits for.</summary>
    public MatchInstructionEvent Event { get; private set; }

    /// <summary>Gets the family the event is narrowed to, or null.</summary>
    public PositionFamily? EventFamily { get; private set; }

    /// <summary>Gets the event's threshold, or null.</summary>
    public int? EventThreshold { get; private set; }

    /// <summary>Gets the formation the instruction moves to, or null when it keeps the shape.</summary>
    public FormationPreset? Formation { get; private set; }

    /// <summary>Gets the manager's placements in the new shape, as the stored <c>jsonb</c>.</summary>
    public string OverridesJson { get; private set; } = "[]";

    /// <summary>Gets the mentality the instruction adopts, or null for no change.</summary>
    public Mentality? Mentality { get; private set; }

    /// <summary>Gets how many steps the mentality moves, positive more attacking.</summary>
    public int MentalitySteps { get; private set; }

    /// <summary>Gets the tempo the instruction adopts, or null for no change.</summary>
    public Tempo? Tempo { get; private set; }

    /// <summary>Gets the passing style the instruction adopts, or null for no change.</summary>
    public PassingStyle? Passing { get; private set; }

    /// <summary>Gets the width the instruction adopts, or null for no change.</summary>
    public Width? Width { get; private set; }

    /// <summary>Gets the pass focus the instruction adopts, or null for no change.</summary>
    public PassFocus? PassFocus { get; private set; }

    /// <summary>Gets the pressing the instruction adopts, or null for no change.</summary>
    public Pressing? Pressing { get; private set; }

    /// <summary>Gets the defensive line the instruction adopts, or null for no change.</summary>
    public DefensiveLine? DefensiveLine { get; private set; }

    /// <summary>Gets the tackling the instruction adopts, or null for no change.</summary>
    public TacklingStyle? Tackling { get; private set; }

    /// <summary>Gets the time-wasting setting the instruction adopts, or null for no change.</summary>
    public TimeWasting? TimeWasting { get; private set; }

    /// <summary>Gets whether the instruction turns the counter-attack on or off, or null for no change.</summary>
    public bool? CounterAttack { get; private set; }

    /// <summary>Gets the substitutions the instruction makes, as the stored <c>jsonb</c>.</summary>
    public string SubstitutionsJson { get; private set; } = "[]";

    /// <summary>Gets when the instruction was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the instruction was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the manager's placements in the new shape.</summary>
    public IReadOnlyList<MatchInstructionOverride> Overrides => ReadOverrides(OverridesJson);

    /// <summary>Gets the substitutions the instruction makes.</summary>
    public IReadOnlyList<MatchInstructionSubstitution> Substitutions => ReadSubstitutions(SubstitutionsJson);

    /// <summary>Gets the instruction as the plain value it was submitted as.</summary>
    public MatchInstructionDefinition Definition => new()
    {
        Name = Name,
        IsEnabled = IsEnabled,
        FromMinute = FromMinute,
        UntilMinute = UntilMinute,
        Score = Score,
        ScoreMargin = ScoreMargin,
        Event = Event,
        EventFamily = EventFamily,
        EventThreshold = EventThreshold,
        Formation = Formation,
        Overrides = Overrides,
        Changes = new MatchInstructionChanges
        {
            Mentality = Mentality,
            MentalitySteps = MentalitySteps,
            Tempo = Tempo,
            Passing = Passing,
            Width = Width,
            PassFocus = PassFocus,
            Pressing = Pressing,
            DefensiveLine = DefensiveLine,
            Tackling = Tackling,
            TimeWasting = TimeWasting,
            CounterAttack = CounterAttack,
        },
        Substitutions = Substitutions,
    };

    /// <summary>Creates an instruction on a plan.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="planId">The owning plan.</param>
    /// <param name="ordinal">The place in the plan's list, from 1.</param>
    /// <param name="definition">What the instruction says. It must already have passed <see cref="MatchInstructionValidator"/>.</param>
    /// <param name="now">The current instant.</param>
    public static MatchInstruction Place(Guid id, Guid planId, int ordinal, MatchInstructionDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (ordinal < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "An instruction's place in the list starts at 1.");
        }

        var instruction = new MatchInstruction
        {
            Id = id,
            PlanId = planId,
            Ordinal = ordinal,
            CreatedAt = now,
        };

        instruction.Apply(definition, now);

        return instruction;
    }

    /// <summary>Replaces what the instruction says, keeping its place and identity.</summary>
    /// <param name="definition">What the instruction says. It must already have passed <see cref="MatchInstructionValidator"/>.</param>
    /// <param name="now">The current instant.</param>
    public void Revise(MatchInstructionDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Apply(definition, now);
    }

    /// <summary>Writes a list of placements as the stored <c>jsonb</c>.</summary>
    /// <param name="overrides">The placements.</param>
    public static string WriteOverrides(IReadOnlyList<MatchInstructionOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        return JsonSerializer.Serialize(
            overrides.Select(placement => new OverrideDocument(placement.PlayerId, placement.SlotNumber)),
            Json);
    }

    /// <summary>Writes a list of substitutions as the stored <c>jsonb</c>.</summary>
    /// <param name="substitutions">The substitutions.</param>
    public static string WriteSubstitutions(IReadOnlyList<MatchInstructionSubstitution> substitutions)
    {
        ArgumentNullException.ThrowIfNull(substitutions);

        return JsonSerializer.Serialize(
            substitutions.Select(substitution => new SubstitutionDocument(
                substitution.Off.ToCode(),
                substitution.OffPlayerId,
                substitution.OffFamily?.ToCode(),
                substitution.On.ToCode(),
                substitution.OnPlayerId,
                substitution.OnFamily?.ToCode())),
            Json);
    }

    private static IReadOnlyList<MatchInstructionOverride> ReadOverrides(string json) =>
        [.. (JsonSerializer.Deserialize<List<OverrideDocument>>(json, Json) ?? [])
            .Select(document => new MatchInstructionOverride(document.PlayerId, document.Slot))];

    private static IReadOnlyList<MatchInstructionSubstitution> ReadSubstitutions(string json) =>
        [.. (JsonSerializer.Deserialize<List<SubstitutionDocument>>(json, Json) ?? [])
            .Select(document => new MatchInstructionSubstitution(
                MatchInstructionCodes.OffFromCode(document.Off),
                document.OffPlayerId,
                document.OffFamily is { } offFamily ? PositionFamilies.FromCode(offFamily) : null,
                MatchInstructionCodes.OnFromCode(document.On),
                document.OnPlayerId,
                document.OnFamily is { } onFamily ? PositionFamilies.FromCode(onFamily) : null))];

    private void Apply(MatchInstructionDefinition definition, DateTimeOffset now)
    {
        var changes = definition.Changes ?? MatchInstructionChanges.None;

        Name = definition.Name.Trim();
        IsEnabled = definition.IsEnabled;
        FromMinute = definition.FromMinute;
        UntilMinute = definition.UntilMinute;
        Score = definition.Score;
        ScoreMargin = definition.ScoreMargin;
        Event = definition.Event;
        EventFamily = definition.EventFamily;
        EventThreshold = definition.EventThreshold;
        Formation = definition.Formation;
        OverridesJson = WriteOverrides(definition.Overrides);
        Mentality = changes.Mentality;
        MentalitySteps = changes.MentalitySteps;
        Tempo = changes.Tempo;
        Passing = changes.Passing;
        Width = changes.Width;
        PassFocus = changes.PassFocus;
        Pressing = changes.Pressing;
        DefensiveLine = changes.DefensiveLine;
        Tackling = changes.Tackling;
        TimeWasting = changes.TimeWasting;
        CounterAttack = changes.CounterAttack;
        SubstitutionsJson = WriteSubstitutions(definition.Substitutions);
        UpdatedAt = now;
    }

    private sealed record OverrideDocument(Guid PlayerId, int Slot);

    private sealed record SubstitutionDocument(
        string Off,
        Guid? OffPlayerId,
        string? OffFamily,
        string On,
        Guid? OnPlayerId,
        string? OnFamily);
}

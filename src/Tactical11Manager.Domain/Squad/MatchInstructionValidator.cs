namespace Tactical11Manager.Domain.Squad;

/// <summary>Why a plan's match instructions are not valid (`MAT-6`, `INS-12`).</summary>
/// <remarks>
/// Like <see cref="TacticalPlanIssueCode"/>, an enum so the validator cannot invent a code at a call site; it is mapped to a stable wire
/// code by <see cref="MatchInstructionIssueCodes"/>. Each issue names the instruction it concerns by its place in the list.
/// </remarks>
public enum MatchInstructionIssueCode
{
    /// <summary>The plan holds more than <see cref="MatchInstruction.MaxPerPlan"/> instructions.</summary>
    TooManyInstructions = 0,

    /// <summary>The name is empty or longer than <see cref="MatchInstruction.MaxNameLength"/> characters.</summary>
    NameInvalid = 1,

    /// <summary>The minutes are outside 0…90, or the last minute is before the first.</summary>
    MinuteRange = 2,

    /// <summary>The goal margin is outside 1…5.</summary>
    ScoreMargin = 3,

    /// <summary>The event's family or threshold is missing, out of range, or means nothing for the event.</summary>
    EventParameter = 4,

    /// <summary>The mentality moves by more steps than allowed.</summary>
    MentalitySteps = 5,

    /// <summary>The instruction makes more than <see cref="MatchInstruction.MaxSubstitutions"/> substitutions.</summary>
    TooManySubstitutions = 6,

    /// <summary>A substitution picks a player in a way that is missing a part: a family it needs, or an event for the subject.</summary>
    SubstitutionSelector = 7,

    /// <summary>A substitution names a player to take off who is not in the plan's lineup.</summary>
    OffPlayerNotInLineup = 8,

    /// <summary>A substitution names a player to bring on who is not on the plan's bench.</summary>
    OnPlayerNotOnBench = 9,

    /// <summary>
    /// A placement in the new shape is for a player outside the lineup who the instruction does not bring on from the bench by name, a slot
    /// outside 2…11, or a repeat, or the instruction has no new shape.
    /// </summary>
    OverrideInvalid = 10,

    /// <summary>
    /// The enabled instructions make more than <see cref="MatchInstruction.MatchSubstitutionLimit"/> substitutions between them, the most a
    /// club may make in a match (`SQ-5`). The issue names the instruction whose substitutions take the total past the limit.
    /// </summary>
    TooManySubstitutionsInPlan = 11,
}

/// <summary>Stable wire codes for <see cref="MatchInstructionIssueCode"/>.</summary>
public static class MatchInstructionIssueCodes
{
    /// <summary>Converts an issue code to the stable code a client branches on.</summary>
    /// <param name="code">The issue code.</param>
    public static string ToCode(this MatchInstructionIssueCode code) => code switch
    {
        MatchInstructionIssueCode.TooManyInstructions => "TOO_MANY_INSTRUCTIONS",
        MatchInstructionIssueCode.NameInvalid => "INSTRUCTION_NAME_INVALID",
        MatchInstructionIssueCode.MinuteRange => "INSTRUCTION_MINUTE_RANGE",
        MatchInstructionIssueCode.ScoreMargin => "INSTRUCTION_SCORE_MARGIN",
        MatchInstructionIssueCode.EventParameter => "INSTRUCTION_EVENT_PARAMETER",
        MatchInstructionIssueCode.MentalitySteps => "INSTRUCTION_MENTALITY_STEPS",
        MatchInstructionIssueCode.TooManySubstitutions => "INSTRUCTION_TOO_MANY_SUBSTITUTIONS",
        MatchInstructionIssueCode.SubstitutionSelector => "INSTRUCTION_SUBSTITUTION_SELECTOR",
        MatchInstructionIssueCode.OffPlayerNotInLineup => "INSTRUCTION_OFF_PLAYER_NOT_IN_LINEUP",
        MatchInstructionIssueCode.OnPlayerNotOnBench => "INSTRUCTION_ON_PLAYER_NOT_ON_BENCH",
        MatchInstructionIssueCode.OverrideInvalid => "INSTRUCTION_OVERRIDE_INVALID",
        MatchInstructionIssueCode.TooManySubstitutionsInPlan => "INSTRUCTIONS_SUBSTITUTION_LIMIT",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown match instruction issue code."),
    };
}

/// <summary>Something about a plan's match instructions worth a manager's attention that does not stop the plan being saved.</summary>
public enum MatchInstructionWarningCode
{
    /// <summary>The instruction changes no shape, no team instruction, and makes no substitution.</summary>
    DoesNothing = 0,

    /// <summary>A placement puts a player in a slot he is not at home in, by family or by the role's natural positions (`INS-10`).</summary>
    OutOfPosition = 1,

    /// <summary>A placement names a player the same instruction takes off by name, so the placement is never used.</summary>
    PlacedPlayerLeaves = 2,
}

/// <summary>Stable wire codes for <see cref="MatchInstructionWarningCode"/>.</summary>
public static class MatchInstructionWarningCodes
{
    /// <summary>Converts a warning code to the stable code a client branches on.</summary>
    /// <param name="code">The warning code.</param>
    public static string ToCode(this MatchInstructionWarningCode code) => code switch
    {
        MatchInstructionWarningCode.DoesNothing => "INSTRUCTION_DOES_NOTHING",
        MatchInstructionWarningCode.OutOfPosition => "INSTRUCTION_OUT_OF_POSITION",
        MatchInstructionWarningCode.PlacedPlayerLeaves => "INSTRUCTION_PLACED_PLAYER_LEAVES",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown match instruction warning code."),
    };
}

/// <summary>One reason a plan's match instructions are not valid.</summary>
/// <param name="Code">What is wrong.</param>
/// <param name="Ordinal">The instruction's place in the list, from 1, or null when the issue is about the list.</param>
/// <param name="PlayerId">The player the issue concerns, when it concerns one.</param>
public sealed record MatchInstructionIssue(MatchInstructionIssueCode Code, int? Ordinal, Guid? PlayerId);

/// <summary>One thing about a plan's match instructions that deserves a look but does not stop a save.</summary>
/// <param name="Code">What it is.</param>
/// <param name="Ordinal">The instruction's place in the list, from 1.</param>
/// <param name="PlayerId">The player the warning concerns, when it concerns one.</param>
public sealed record MatchInstructionWarning(MatchInstructionWarningCode Code, int Ordinal, Guid? PlayerId = null);

/// <summary>Where a player plays, which is what the out-of-position warning reads.</summary>
/// <param name="Primary">His primary position.</param>
/// <param name="Secondary">His other positions.</param>
public sealed record PlayerPositionFacts(PlayerPosition Primary, IReadOnlyList<PlayerPosition> Secondary);

/// <summary>The verdict on a plan's match instructions.</summary>
/// <param name="Issues">Every reason the instructions are not valid, in list order. Empty when they are.</param>
/// <param name="Warnings">Everything that deserves a look, in list order.</param>
public sealed record MatchInstructionValidation(
    IReadOnlyList<MatchInstructionIssue> Issues,
    IReadOnlyList<MatchInstructionWarning> Warnings)
{
    /// <summary>Gets whether the instructions are valid and may be saved.</summary>
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// The match instructions validator (`MAT-6`, `INS-12`).
/// </summary>
/// <remarks>
/// <para>
/// One pure function, so a manager and an AI club are held to the same rules (`INS-12`). It checks everything the engine's input checks
/// would refuse, so a plan that passes here can never make a fixture's snapshot fail at lock time, when nobody can still do anything about it.
/// </para>
/// <para>
/// A named player has to be someone the plan fields: a player to take off is in its lineup, and a player to bring on is on its bench. What a
/// club does when that player is unavailable on the day is the snapshot's to decide, and it drops the substitution.
/// </para>
/// </remarks>
public static class MatchInstructionValidator
{
    /// <summary>Validates a plan's match instructions.</summary>
    /// <param name="instructions">The instructions, in the order they are checked.</param>
    /// <param name="lineupPlayerIds">The players in the plan's lineup.</param>
    /// <param name="benchPlayerIds">The players on the plan's bench.</param>
    /// <param name="players">Where the lineup and bench play, for the out-of-position warning, or null to leave that warning out.</param>
    public static MatchInstructionValidation Validate(
        IReadOnlyList<MatchInstructionDefinition> instructions,
        IReadOnlyCollection<Guid> lineupPlayerIds,
        IReadOnlyCollection<Guid> benchPlayerIds,
        IReadOnlyDictionary<Guid, PlayerPositionFacts>? players = null)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(lineupPlayerIds);
        ArgumentNullException.ThrowIfNull(benchPlayerIds);

        var issues = new List<MatchInstructionIssue>();
        var lineup = lineupPlayerIds as ISet<Guid> ?? new HashSet<Guid>(lineupPlayerIds);
        var bench = benchPlayerIds as ISet<Guid> ?? new HashSet<Guid>(benchPlayerIds);

        if (instructions.Count > MatchInstruction.MaxPerPlan)
        {
            issues.Add(new MatchInstructionIssue(MatchInstructionIssueCode.TooManyInstructions, null, null));
        }

        var substitutions = 0;
        var exceeded = false;

        for (var index = 0; index < instructions.Count; index++)
        {
            ValidateInstruction(instructions[index], index + 1, lineup, bench, issues);

            // A club makes at most five substitutions in a match (SQ-5), so rules that are on may not plan more between them. One that
            // is off plans none, and turning it on later is where the plan is refused.
            if (instructions[index].IsEnabled)
            {
                substitutions += instructions[index].Substitutions.Count;
            }

            if (!exceeded && substitutions > MatchInstruction.MatchSubstitutionLimit)
            {
                exceeded = true;
                issues.Add(new MatchInstructionIssue(MatchInstructionIssueCode.TooManySubstitutionsInPlan, index + 1, null));
            }
        }

        return new MatchInstructionValidation(issues, WarningsOf(instructions, players));
    }

    /// <summary>Finds what deserves a manager's attention in a plan's instructions.</summary>
    /// <param name="instructions">The instructions, in the order they are checked.</param>
    /// <param name="players">Where the players the instructions place play, or null to leave the out-of-position warning out.</param>
    public static IReadOnlyList<MatchInstructionWarning> WarningsOf(
        IReadOnlyList<MatchInstructionDefinition> instructions,
        IReadOnlyDictionary<Guid, PlayerPositionFacts>? players = null)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var warnings = new List<MatchInstructionWarning>();

        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            var ordinal = index + 1;

            if (!instruction.IsEnabled)
            {
                continue;
            }

            if (instruction.Formation is null && (instruction.Changes ?? MatchInstructionChanges.None).IsEmpty
                && instruction.Substitutions.Count == 0)
            {
                warnings.Add(new MatchInstructionWarning(MatchInstructionWarningCode.DoesNothing, ordinal));
            }

            AddPlacementWarnings(instruction, ordinal, players, warnings);
        }

        return warnings;
    }

    /// <summary>
    /// The warnings about a rule's placements: a player the same rule takes off by name is placed for nothing, and a player put in a slot he
    /// is not at home in plays out of position (`INS-10`), which is how the main board marks it too.
    /// </summary>
    private static void AddPlacementWarnings(
        MatchInstructionDefinition instruction,
        int ordinal,
        IReadOnlyDictionary<Guid, PlayerPositionFacts>? players,
        List<MatchInstructionWarning> warnings)
    {
        if (instruction.Formation is not { } formation || instruction.Overrides.Count == 0)
        {
            return;
        }

        var leaving = instruction.Substitutions
            .Where(substitution => substitution.Off == MatchInstructionOff.Player && substitution.OffPlayerId is not null)
            .Select(substitution => substitution.OffPlayerId!.Value)
            .ToHashSet();
        var slots = FormationLayouts.DefaultSlots(formation).ToDictionary(slot => slot.SlotNumber);

        foreach (var placement in instruction.Overrides)
        {
            if (leaving.Contains(placement.PlayerId))
            {
                warnings.Add(new MatchInstructionWarning(MatchInstructionWarningCode.PlacedPlayerLeaves, ordinal, placement.PlayerId));

                continue;
            }

            if (players is not null
                && players.TryGetValue(placement.PlayerId, out var facts)
                && slots.TryGetValue(placement.SlotNumber, out var slot)
                && !RolePositions.IsAtHome(facts.Primary, facts.Secondary, slot.PositionFamily, slot.Role))
            {
                warnings.Add(new MatchInstructionWarning(MatchInstructionWarningCode.OutOfPosition, ordinal, placement.PlayerId));
            }
        }
    }

    private static void ValidateInstruction(
        MatchInstructionDefinition instruction,
        int ordinal,
        ISet<Guid> lineup,
        ISet<Guid> bench,
        List<MatchInstructionIssue> issues)
    {
        void Add(MatchInstructionIssueCode code, Guid? playerId = null) =>
            issues.Add(new MatchInstructionIssue(code, ordinal, playerId));

        var name = instruction.Name?.Trim() ?? string.Empty;

        if (name.Length == 0 || name.Length > MatchInstruction.MaxNameLength)
        {
            Add(MatchInstructionIssueCode.NameInvalid);
        }

        if (instruction.FromMinute is < 0 or > MatchInstruction.MaxMinute
            || (instruction.UntilMinute is { } until && (until < instruction.FromMinute || until > MatchInstruction.MaxMinute)))
        {
            Add(MatchInstructionIssueCode.MinuteRange);
        }

        if (instruction.ScoreMargin is < 1 or > MatchInstruction.MaxScoreMargin)
        {
            Add(MatchInstructionIssueCode.ScoreMargin);
        }

        if (!EventParametersAreValid(instruction))
        {
            Add(MatchInstructionIssueCode.EventParameter);
        }

        var changes = instruction.Changes ?? MatchInstructionChanges.None;

        if (changes.MentalitySteps is < -MatchInstructionChanges.MaxMentalitySteps or > MatchInstructionChanges.MaxMentalitySteps)
        {
            Add(MatchInstructionIssueCode.MentalitySteps);
        }

        if (instruction.Substitutions.Count > MatchInstruction.MaxSubstitutions)
        {
            Add(MatchInstructionIssueCode.TooManySubstitutions);
        }

        foreach (var substitution in instruction.Substitutions)
        {
            ValidateSubstitution(substitution, instruction.Event, lineup, bench, Add);
        }

        ValidateOverrides(instruction, lineup, bench, Add);
    }

    /// <summary>
    /// Whether the event's family and threshold are the ones its kind needs, and none it does not: a client that changes the kind of event
    /// clears what the old kind left behind.
    /// </summary>
    private static bool EventParametersAreValid(MatchInstructionDefinition instruction)
    {
        var threshold = instruction.EventThreshold;
        var family = instruction.EventFamily;

        return instruction.Event switch
        {
            MatchInstructionEvent.None or MatchInstructionEvent.OwnSentOff or MatchInstructionEvent.OpponentSentOff =>
                threshold is null && family is null,

            MatchInstructionEvent.OwnBooked => threshold is null,

            MatchInstructionEvent.PlayingBadly =>
                (threshold is not null || family is not null) && (threshold is null or >= 0 and <= MatchInstruction.MaxThreshold),

            MatchInstructionEvent.Tired => threshold is >= 0 and <= MatchInstruction.MaxThreshold,

            MatchInstructionEvent.OpponentMentalityAtLeast or MatchInstructionEvent.OpponentMentalityAtMost =>
                family is null && threshold is { } level && Enum.IsDefined((Mentality)level),

            _ => false,
        };
    }

    private static void ValidateSubstitution(
        MatchInstructionSubstitution substitution,
        MatchInstructionEvent trigger,
        ISet<Guid> lineup,
        ISet<Guid> bench,
        Action<MatchInstructionIssueCode, Guid?> add)
    {
        switch (substitution.Off)
        {
            case MatchInstructionOff.Player when substitution.OffPlayerId is null:
                add(MatchInstructionIssueCode.SubstitutionSelector, null);

                break;

            case MatchInstructionOff.Player when !lineup.Contains(substitution.OffPlayerId.Value):
                add(MatchInstructionIssueCode.OffPlayerNotInLineup, substitution.OffPlayerId);

                break;

            case MatchInstructionOff.Subject
                when trigger is not (MatchInstructionEvent.OwnBooked or MatchInstructionEvent.PlayingBadly or MatchInstructionEvent.Tired):
                add(MatchInstructionIssueCode.SubstitutionSelector, null);

                break;

            case MatchInstructionOff.MostTiredInFamily or MatchInstructionOff.LowestRatedInFamily when substitution.OffFamily is null:
                add(MatchInstructionIssueCode.SubstitutionSelector, null);

                break;
        }

        switch (substitution.On)
        {
            case MatchInstructionOn.Player when substitution.OnPlayerId is null:
                add(MatchInstructionIssueCode.SubstitutionSelector, null);

                break;

            case MatchInstructionOn.Player when !bench.Contains(substitution.OnPlayerId.Value):
                add(MatchInstructionIssueCode.OnPlayerNotOnBench, substitution.OnPlayerId);

                break;

            case MatchInstructionOn.BestFitInFamily when substitution.OnFamily is null:
                add(MatchInstructionIssueCode.SubstitutionSelector, null);

                break;
        }
    }

    /// <summary>
    /// A placement names a player the shape can have on the pitch: one in the lineup, or a bench player the same instruction brings on by name.
    /// The substitutions are made before the shape is fitted, so the engine can place him.
    /// </summary>
    private static void ValidateOverrides(
        MatchInstructionDefinition instruction,
        ISet<Guid> lineup,
        ISet<Guid> bench,
        Action<MatchInstructionIssueCode, Guid?> add)
    {
        if (instruction.Overrides.Count == 0)
        {
            return;
        }

        if (instruction.Formation is null)
        {
            add(MatchInstructionIssueCode.OverrideInvalid, null);

            return;
        }

        var incoming = instruction.Substitutions
            .Where(substitution => substitution.On == MatchInstructionOn.Player && substitution.OnPlayerId is { } id && bench.Contains(id))
            .Select(substitution => substitution.OnPlayerId!.Value)
            .ToHashSet();
        var players = new HashSet<Guid>();
        var slots = new HashSet<int>();

        foreach (var placement in instruction.Overrides)
        {
            if (!(lineup.Contains(placement.PlayerId) || incoming.Contains(placement.PlayerId))
                || placement.SlotNumber is < TacticalSlot.FirstSlotNumber + 1 or > TacticalSlot.LastSlotNumber
                || !players.Add(placement.PlayerId)
                || !slots.Add(placement.SlotNumber))
            {
                add(MatchInstructionIssueCode.OverrideInvalid, placement.PlayerId);
            }
        }
    }
}

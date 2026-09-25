namespace TouchlineManager.MatchEngine.Model;

/// <summary>
/// One player's frozen attributes, in the canonical attribute order.
/// </summary>
/// <remarks>
/// Held as a flat run of twenty-eight values rather than as twenty-eight named properties, because the
/// engine reads attributes by name through the rating weight tables rather than one at a time, and
/// because the canonical snapshot hash is taken over exactly this order. The values are the player's at
/// the moment the fixture locked (MAT-1) — training, injuries, and transfers after the lock cannot
/// change what was played.
/// </remarks>
public sealed class PlayerAttributesV1
{
    private PlayerAttributesV1(IReadOnlyList<int> values) => Values = values;

    /// <summary>Gets the attributes, in <see cref="MatchAttributeName"/> order.</summary>
    public IReadOnlyList<int> Values { get; }

    /// <summary>Creates an attribute set from values in canonical order.</summary>
    /// <param name="values">Exactly twenty-eight values, each in 1..20 (`TRN-4`).</param>
    /// <exception cref="InvalidMatchInputException">When the count or a value is out of range.</exception>
    public static PlayerAttributesV1 From(IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count != MatchAttributeNames.Count)
        {
            throw new InvalidMatchInputException(
                $"A player has exactly {MatchAttributeNames.Count} attributes, got {values.Count}.");
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is < MatchAttributeNames.Min or > MatchAttributeNames.Max)
            {
                throw new InvalidMatchInputException(
                    $"Attribute {(MatchAttributeName)index} is {values[index]}, outside "
                    + $"{MatchAttributeNames.Min}..{MatchAttributeNames.Max} (TRN-4).");
            }
        }

        return new PlayerAttributesV1([.. values]);
    }

    /// <summary>Builds a set in which every attribute has the same value.</summary>
    /// <param name="value">The value for every attribute.</param>
    public static PlayerAttributesV1 Uniform(int value) =>
        From([.. Enumerable.Repeat(value, MatchAttributeNames.Count)]);

    /// <summary>Gets one attribute's value.</summary>
    /// <param name="attribute">The attribute.</param>
    public int ValueOf(MatchAttributeName attribute) => Values[(int)attribute];
}

/// <summary>
/// A player's frozen condition, fatigue, morale, and match sharpness, in basis points.
/// </summary>
/// <remarks>
/// The same four values the domain stores (`TRN-5`…`TRN-7`), carried across in the snapshot so the
/// engine can apply their bounded effects without reaching back into mutable live state. The engine
/// models its own working copies and never writes these values anywhere.
/// </remarks>
public sealed record PlayerMatchStateV1
{
    /// <summary>Gets condition: how fresh the player is right now.</summary>
    public required int ConditionBasisPoints { get; init; }

    /// <summary>Gets accumulated fatigue, which recovery has not yet shed.</summary>
    public required int FatigueBasisPoints { get; init; }

    /// <summary>Gets morale.</summary>
    public required int MoraleBasisPoints { get; init; }

    /// <summary>Gets match sharpness.</summary>
    public required int SharpnessBasisPoints { get; init; }

    /// <summary>Creates a state whose four values are all the same.</summary>
    /// <param name="basisPoints">The value for all four.</param>
    public static PlayerMatchStateV1 Uniform(int basisPoints) => new()
    {
        ConditionBasisPoints = basisPoints,
        FatigueBasisPoints = basisPoints,
        MoraleBasisPoints = basisPoints,
        SharpnessBasisPoints = basisPoints,
    };
}

/// <summary>
/// One player as the match sees them: identity, position, attributes, and state, all frozen.
/// </summary>
/// <remarks>
/// A participant is both a member of the squad and, once the lineup is named, the occupant of a slot.
/// The engine refers to a player by <see cref="ParticipantId"/> everywhere — in events, in statistics,
/// and in the commentary tokens that describe them — so a substitution changes who occupies a slot
/// without changing the identity the record refers to.
/// </remarks>
public sealed record MatchParticipantV1
{
    /// <summary>Gets the match participant's stable identity, which events refer to.</summary>
    public required Guid ParticipantId { get; init; }

    /// <summary>Gets the underlying player's identity.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>Gets the club the participant plays for.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>Gets the display name commentary uses.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the shirt number.</summary>
    public required int ShirtNumber { get; init; }

    /// <summary>Gets the position the player is most at home in.</summary>
    public required MatchPosition Position { get; init; }

    /// <summary>Gets the further positions the player is comfortable in.</summary>
    public IReadOnlyList<MatchPosition> SecondaryPositions { get; init; } = [];

    /// <summary>Gets the frozen attributes.</summary>
    public required PlayerAttributesV1 Attributes { get; init; }

    /// <summary>Gets the frozen condition, fatigue, morale, and sharpness.</summary>
    public required PlayerMatchStateV1 State { get; init; }

    /// <summary>Gets whether the player is a goalkeeper, which the lineup rules depend on.</summary>
    public bool IsGoalkeeper => Position == MatchPosition.Goalkeeper;
}

/// <summary>
/// One occupied position on the pitch: a slot number, the role it asks for, where it stands, and who
/// occupies it.
/// </summary>
/// <remarks>
/// The coordinates are the same normalized 0–10_000 values the tactics board stores and the engine
/// hashes (TAC-9), so what a manager arranged is what the engine reads rather than a second,
/// display-only copy. Slot numbers are stable across formation changes, which is what lets a prepared
/// team sheet keep referring to the same eleven positions.
/// </remarks>
public sealed record MatchSlotV1
{
    /// <summary>Gets the slot number, 1…11.</summary>
    public required int SlotNumber { get; init; }

    /// <summary>Gets the position family the slot belongs to.</summary>
    public required MatchPositionFamily Family { get; init; }

    /// <summary>Gets the role the slot asks its occupant to perform.</summary>
    public required MatchRole Role { get; init; }

    /// <summary>Gets the normalized position across the pitch, 0…10_000.</summary>
    public required int X { get; init; }

    /// <summary>Gets the normalized position down the pitch, 0…10_000.</summary>
    public required int Y { get; init; }

    /// <summary>Gets the identity of the player occupying the slot.</summary>
    public required Guid ParticipantId { get; init; }
}

/// <summary>The eight team instructions a side takes into a match (`INS-1`…`INS-8`).</summary>
public sealed record MatchInstructionsV1
{
    /// <summary>Gets the overall approach.</summary>
    public MatchMentality Mentality { get; init; } = MatchMentality.Balanced;

    /// <summary>Gets how quickly the team moves the ball.</summary>
    public MatchTempo Tempo { get; init; } = MatchTempo.Normal;

    /// <summary>Gets what kind of pass the team favours.</summary>
    public MatchPassingStyle Passing { get; init; } = MatchPassingStyle.MixedPassing;

    /// <summary>Gets how far the team spreads across the pitch.</summary>
    public MatchWidth Width { get; init; } = MatchWidth.Normal;

    /// <summary>Gets where the team begins to press.</summary>
    public MatchPressing Pressing { get; init; } = MatchPressing.MidBlock;

    /// <summary>Gets how high the defensive line holds.</summary>
    public MatchDefensiveLine DefensiveLine { get; init; } = MatchDefensiveLine.Normal;

    /// <summary>Gets how committed the tackling is.</summary>
    public MatchTacklingStyle Tackling { get; init; } = MatchTacklingStyle.Normal;

    /// <summary>Gets whether the team runs down the clock.</summary>
    public MatchTimeWasting TimeWasting { get; init; } = MatchTimeWasting.Off;
}

/// <summary>One side's frozen squad, lineup, and instructions.</summary>
public sealed record MatchSideV1
{
    /// <summary>Gets the club's identity.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>Gets the club's name, for commentary.</summary>
    public required string ClubName { get; init; }

    /// <summary>Gets every available player: the eleven starters and up to seven substitutes.</summary>
    public required IReadOnlyList<MatchParticipantV1> Squad { get; init; }

    /// <summary>Gets the eleven occupied slots, in any order.</summary>
    public required IReadOnlyList<MatchSlotV1> Slots { get; init; }

    /// <summary>Gets the team instructions.</summary>
    public required MatchInstructionsV1 Instructions { get; init; }

    /// <summary>Gets the participants named in the eleven slots, matched by identity.</summary>
    /// <returns>The starters, in slot-number order.</returns>
    public IReadOnlyList<MatchParticipantV1> Starters()
    {
        var byId = Squad.ToDictionary(participant => participant.ParticipantId);

        return
        [
            .. Slots
                .OrderBy(slot => slot.SlotNumber)
                .Select(slot => byId[slot.ParticipantId]),
        ];
    }
}

/// <summary>
/// The immutable input snapshot a match is simulated from (MAT-1, MAT-9).
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what the engine is allowed to read. It carries no clock, no database handle, and
/// no mutable reference into live state, which is what makes a result reproducible and a delayed lock
/// block kickoff rather than silently simulate from whatever the tables happen to hold (ADR-0004).
/// </para>
/// <para>
/// The seed is derived by the caller with <c>MatchSeed.Derive</c> from the world secret, the fixture,
/// the locked snapshot hash, and the engine version, so nobody — including a manager — can choose a
/// favourable one (master plan §8.2). It is carried in the input and therefore covered by the input
/// hash.
/// </para>
/// </remarks>
public sealed record MatchInputV1
{
    /// <summary>How many players a side fields.</summary>
    public const int StartersOnPitch = 11;

    /// <summary>The most substitutes a side may name (`SQ-4`).</summary>
    public const int MaxSubstitutesOnBench = 7;

    /// <summary>Gets the fixture's identity.</summary>
    public required Guid FixtureId { get; init; }

    /// <summary>Gets the world's identity.</summary>
    public required Guid WorldId { get; init; }

    /// <summary>Gets the season's identity.</summary>
    public required Guid SeasonId { get; init; }

    /// <summary>Gets the engine version label the caller pinned.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>Gets the rules version label the caller pinned.</summary>
    public required string RuleSetVersion { get; init; }

    /// <summary>Gets the home side.</summary>
    public required MatchSideV1 Home { get; init; }

    /// <summary>Gets the away side.</summary>
    public required MatchSideV1 Away { get; init; }

    /// <summary>Gets home advantage, in basis points (master plan §8.5).</summary>
    public required int HomeAdvantageBasisPoints { get; init; }

    /// <summary>Gets the hash of the formula configuration in force, so a result records its own rules.</summary>
    public required string FormulaConfigurationHash { get; init; }

    /// <summary>Gets the secret match seed.</summary>
    public required ulong Seed { get; init; }

    /// <summary>Gets the side that is at home.</summary>
    public MatchSideV1 HomeSide => Home;

    /// <summary>Gets the side at the given end.</summary>
    /// <param name="side">Which end.</param>
    public MatchSideV1 SideOf(MatchSide side) => side == MatchSide.Home ? Home : Away;

    /// <summary>Gets the side that is not the given one.</summary>
    /// <param name="side">Which end.</param>
    public static MatchSide OpponentOf(MatchSide side) =>
        side == MatchSide.Home ? MatchSide.Away : MatchSide.Home;

    /// <summary>
    /// Checks the snapshot is structurally simulatable, and throws naming the first problem.
    /// </summary>
    /// <remarks>
    /// The engine's front door. Every one of these is a caller error rather than a game outcome — a
    /// lineup of ten, a slot naming a player who is not in the squad, a substitution bench of eight — and
    /// each would otherwise produce a plausible-looking but meaningless match. A malformed snapshot
    /// rejected here is what makes the fuzz suite's contract "valid input or a named refusal, never a
    /// silent wrong answer".
    /// </remarks>
    /// <exception cref="InvalidMatchInputException">When the snapshot cannot be simulated.</exception>
    public void Validate()
    {
        if (FixtureId == Guid.Empty)
        {
            throw new InvalidMatchInputException("The fixture identity is required.");
        }

        if (WorldId == Guid.Empty)
        {
            throw new InvalidMatchInputException("The world identity is required.");
        }

        if (SeasonId == Guid.Empty)
        {
            throw new InvalidMatchInputException("The season identity is required.");
        }

        // Home advantage is a multiplier on the home side's ratings, not a probability, so it is bounded by
        // the multiplier band rather than by certainty.
        if (HomeAdvantageBasisPoints is < Configuration.EngineRulesV1.MinMultiplier
            or > Configuration.EngineRulesV1.MaxMultiplier)
        {
            throw new InvalidMatchInputException(
                $"Home advantage is {HomeAdvantageBasisPoints} basis points, outside "
                + $"{Configuration.EngineRulesV1.MinMultiplier}..{Configuration.EngineRulesV1.MaxMultiplier}.");
        }

        if (Home.ClubId == Away.ClubId)
        {
            throw new InvalidMatchInputException("A club cannot play itself.");
        }

        ValidateSide(Home, MatchSide.Home);
        ValidateSide(Away, MatchSide.Away);
    }

    private static void ValidateSide(MatchSideV1 side, MatchSide which)
    {
        var label = which == MatchSide.Home ? "The home side" : "The away side";

        if (side.ClubId == Guid.Empty)
        {
            throw new InvalidMatchInputException($"{label} has no club identity.");
        }

        if (side.Squad.Count < StartersOnPitch)
        {
            throw new InvalidMatchInputException(
                $"{label} names {side.Squad.Count} players, fewer than the {StartersOnPitch} required.");
        }

        var bench = side.Squad.Count - StartersOnPitch;

        if (bench > MaxSubstitutesOnBench)
        {
            throw new InvalidMatchInputException(
                $"{label} names {bench} substitutes, more than the {MaxSubstitutesOnBench} permitted (SQ-4).");
        }

        if (side.Slots.Count != StartersOnPitch)
        {
            throw new InvalidMatchInputException(
                $"{label} has {side.Slots.Count} slots; a lineup has exactly {StartersOnPitch} (SQ-4).");
        }

        var participantIds = new HashSet<Guid>();

        foreach (var participant in side.Squad)
        {
            if (participant.ParticipantId == Guid.Empty)
            {
                throw new InvalidMatchInputException($"{label} has a participant with no identity.");
            }

            if (!participantIds.Add(participant.ParticipantId))
            {
                throw new InvalidMatchInputException(
                    $"{label} names participant {participant.ParticipantId} more than once.");
            }

            if (participant.ClubId != side.ClubId)
            {
                throw new InvalidMatchInputException(
                    $"{label} carries participant {participant.ParticipantId} from another club.");
            }
        }

        var slotNumbers = new HashSet<int>();
        var slotsByPosition = new HashSet<(int X, int Y)>();

        foreach (var slot in side.Slots)
        {
            if (slot.SlotNumber is < 1 or > StartersOnPitch)
            {
                throw new InvalidMatchInputException(
                    $"{label} has slot number {slot.SlotNumber}, outside 1..{StartersOnPitch}.");
            }

            if (!slotNumbers.Add(slot.SlotNumber))
            {
                throw new InvalidMatchInputException($"{label} repeats slot number {slot.SlotNumber}.");
            }

            if (slot.Role.FamilyOf() != slot.Family)
            {
                throw new InvalidMatchInputException(
                    $"{label}'s slot {slot.SlotNumber} asks for {slot.Role}, which is not a {slot.Family} role (TAC-8).");
            }

            if (slot.X is < 0 or > Configuration.EngineRulesV1.SlotCoordinateScale
                || slot.Y is < 0 or > Configuration.EngineRulesV1.SlotCoordinateScale)
            {
                throw new InvalidMatchInputException(
                    $"{label}'s slot {slot.SlotNumber} is at ({slot.X},{slot.Y}), outside the pitch (TAC-9).");
            }

            if (!slotsByPosition.Add((slot.X, slot.Y)))
            {
                throw new InvalidMatchInputException(
                    $"{label} has two slots at ({slot.X},{slot.Y}) (TAC-9).");
            }

            if (!participantIds.Contains(slot.ParticipantId))
            {
                throw new InvalidMatchInputException(
                    $"{label}'s slot {slot.SlotNumber} names participant {slot.ParticipantId}, who is not in the squad.");
            }
        }

        var starters = side.Starters();

        if (starters.Select(participant => participant.ParticipantId).Distinct().Count() != StartersOnPitch)
        {
            throw new InvalidMatchInputException($"{label} names the same player in two slots (SQ-4).");
        }

        if (starters.Count(participant => participant.IsGoalkeeper) != 1)
        {
            throw new InvalidMatchInputException(
                $"{label} must field exactly one recognised goalkeeper in the eleven (SQ-2).");
        }
    }
}

/// <summary>
/// Raised when a match snapshot cannot be simulated.
/// </summary>
/// <remarks>
/// A distinct type rather than <see cref="ArgumentException"/> so a caller can tell "this snapshot is
/// malformed" from "this argument is null" without parsing a message, and so the fuzz suite can assert on
/// refusals precisely.
/// </remarks>
public sealed class InvalidMatchInputException : Exception
{
    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="message">What is wrong with the snapshot.</param>
    public InvalidMatchInputException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and an inner cause.</summary>
    /// <param name="message">What is wrong with the snapshot.</param>
    /// <param name="innerException">The cause.</param>
    public InvalidMatchInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes the exception with no message.</summary>
    public InvalidMatchInputException()
    {
    }
}

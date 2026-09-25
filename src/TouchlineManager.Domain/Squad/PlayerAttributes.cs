using System.Globalization;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Squad;

/// <summary>The band an attribute belongs to, which individual training focus selects (`TRN-2`).</summary>
public enum AttributeFamily
{
    /// <summary>Finishing, passing, crossing, and the other ball skills.</summary>
    Technical = 0,

    /// <summary>Decisions, vision, positioning, and the other mental skills.</summary>
    Mental = 1,

    /// <summary>Pace, stamina, strength, and the other physical skills.</summary>
    Physical = 2,

    /// <summary>Handling, reflexes, one-on-ones, and aerial ability.</summary>
    Goalkeeping = 3,
}

/// <summary>Stable codes and storage representation for <see cref="AttributeFamily"/>.</summary>
public static class AttributeFamilies
{
    /// <summary>The code for <see cref="AttributeFamily.Technical"/>.</summary>
    public const string TechnicalCode = "technical";

    /// <summary>The code for <see cref="AttributeFamily.Mental"/>.</summary>
    public const string MentalCode = "mental";

    /// <summary>The code for <see cref="AttributeFamily.Physical"/>.</summary>
    public const string PhysicalCode = "physical";

    /// <summary>The code for <see cref="AttributeFamily.Goalkeeping"/>.</summary>
    public const string GoalkeepingCode = "goalkeeping";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 11;

    /// <summary>Converts a family to its stable code.</summary>
    /// <param name="family">The family.</param>
    public static string ToCode(this AttributeFamily family) => family switch
    {
        AttributeFamily.Technical => TechnicalCode,
        AttributeFamily.Mental => MentalCode,
        AttributeFamily.Physical => PhysicalCode,
        AttributeFamily.Goalkeeping => GoalkeepingCode,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown attribute family."),
    };

    /// <summary>Parses a stable code back to its family.</summary>
    /// <param name="code">The stable code.</param>
    public static AttributeFamily FromCode(string code) => code switch
    {
        TechnicalCode => AttributeFamily.Technical,
        MentalCode => AttributeFamily.Mental,
        PhysicalCode => AttributeFamily.Physical,
        GoalkeepingCode => AttributeFamily.Goalkeeping,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown attribute family code."),
    };
}

/// <summary>
/// A player's twenty-eight displayed attributes, on the 1–20 scale (`TRN-4`).
/// </summary>
/// <remarks>
/// <para>
/// A flat set rather than four nested objects, because it is one row of twenty-eight columns
/// (`data-model.md` §3.2) and because the engine reads attributes individually rather than by family.
/// There is deliberately no aggregate "overall" value: the glossary is explicit that none is
/// authoritative, and inventing one here would let a single number stand in for a player's shape.
/// </para>
/// <para>
/// The order of the properties is the canonical order used for the stored checksum and for the
/// equality a determinism test relies on. Reordering them is a schema change.
/// </para>
/// </remarks>
public sealed record PlayerAttributeSet
{
    /// <summary>Gets finishing.</summary>
    public int Finishing { get; init; }

    /// <summary>Gets passing.</summary>
    public int Passing { get; init; }

    /// <summary>Gets crossing.</summary>
    public int Crossing { get; init; }

    /// <summary>Gets dribbling.</summary>
    public int Dribbling { get; init; }

    /// <summary>Gets first touch.</summary>
    public int FirstTouch { get; init; }

    /// <summary>Gets tackling.</summary>
    public int Tackling { get; init; }

    /// <summary>Gets marking.</summary>
    public int Marking { get; init; }

    /// <summary>Gets heading.</summary>
    public int Heading { get; init; }

    /// <summary>Gets technique.</summary>
    public int Technique { get; init; }

    /// <summary>Gets set pieces.</summary>
    public int SetPieces { get; init; }

    /// <summary>Gets decisions.</summary>
    public int Decisions { get; init; }

    /// <summary>Gets vision.</summary>
    public int Vision { get; init; }

    /// <summary>Gets positioning.</summary>
    public int Positioning { get; init; }

    /// <summary>Gets composure.</summary>
    public int Composure { get; init; }

    /// <summary>Gets anticipation.</summary>
    public int Anticipation { get; init; }

    /// <summary>Gets work rate.</summary>
    public int WorkRate { get; init; }

    /// <summary>Gets aggression.</summary>
    public int Aggression { get; init; }

    /// <summary>Gets leadership.</summary>
    public int Leadership { get; init; }

    /// <summary>Gets pace.</summary>
    public int Pace { get; init; }

    /// <summary>Gets acceleration.</summary>
    public int Acceleration { get; init; }

    /// <summary>Gets stamina.</summary>
    public int Stamina { get; init; }

    /// <summary>Gets strength.</summary>
    public int Strength { get; init; }

    /// <summary>Gets agility.</summary>
    public int Agility { get; init; }

    /// <summary>Gets jumping reach.</summary>
    public int JumpingReach { get; init; }

    /// <summary>Gets handling.</summary>
    public int Handling { get; init; }

    /// <summary>Gets reflexes.</summary>
    public int Reflexes { get; init; }

    /// <summary>Gets one-on-ones.</summary>
    public int OneOnOnes { get; init; }

    /// <summary>Gets aerial ability.</summary>
    public int AerialAbility { get; init; }

    /// <summary>Gets every attribute in canonical order.</summary>
    public IReadOnlyList<int> Values =>
    [
        Finishing,
        Passing,
        Crossing,
        Dribbling,
        FirstTouch,
        Tackling,
        Marking,
        Heading,
        Technique,
        SetPieces,
        Decisions,
        Vision,
        Positioning,
        Composure,
        Anticipation,
        WorkRate,
        Aggression,
        Leadership,
        Pace,
        Acceleration,
        Stamina,
        Strength,
        Agility,
        JumpingReach,
        Handling,
        Reflexes,
        OneOnOnes,
        AerialAbility,
    ];

    /// <summary>Gets the technical attributes, in canonical order.</summary>
    public IReadOnlyList<int> Technical =>
        [Finishing, Passing, Crossing, Dribbling, FirstTouch, Tackling, Marking, Heading, Technique, SetPieces];

    /// <summary>Gets the mental attributes, in canonical order.</summary>
    public IReadOnlyList<int> Mental =>
        [Decisions, Vision, Positioning, Composure, Anticipation, WorkRate, Aggression, Leadership];

    /// <summary>Gets the physical attributes, in canonical order.</summary>
    public IReadOnlyList<int> Physical => [Pace, Acceleration, Stamina, Strength, Agility, JumpingReach];

    /// <summary>Gets the goalkeeping attributes, in canonical order.</summary>
    public IReadOnlyList<int> Goalkeeping => [Handling, Reflexes, OneOnOnes, AerialAbility];

    /// <summary>Gets the attributes belonging to one family.</summary>
    /// <param name="family">The family.</param>
    public IReadOnlyList<int> OfFamily(AttributeFamily family) => family switch
    {
        AttributeFamily.Technical => Technical,
        AttributeFamily.Mental => Mental,
        AttributeFamily.Physical => Physical,
        AttributeFamily.Goalkeeping => Goalkeeping,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown attribute family."),
    };

    /// <summary>Gets whether every attribute is inside the displayed scale (`TRN-4`).</summary>
    public bool IsWithinScale => Values.All(
        value => value is >= WorldRuleSet.AttributeMin and <= WorldRuleSet.AttributeMax);

    /// <summary>Gets one attribute by its canonical name.</summary>
    /// <param name="name">The attribute.</param>
    public int ValueOf(AttributeName name) => Values[(int)name];

    /// <summary>
    /// Builds a set from values in canonical order, which is the order <see cref="Values"/> returns and
    /// the order a profile table is written in.
    /// </summary>
    /// <param name="values">The attribute values, in canonical order.</param>
    public static PlayerAttributeSet FromValues(IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count != AttributeNames.Count)
        {
            throw new ArgumentException(
                $"An attribute set has exactly {AttributeNames.Count} values.",
                nameof(values));
        }

        return new PlayerAttributeSet
        {
            Finishing = values[(int)AttributeName.Finishing],
            Passing = values[(int)AttributeName.Passing],
            Crossing = values[(int)AttributeName.Crossing],
            Dribbling = values[(int)AttributeName.Dribbling],
            FirstTouch = values[(int)AttributeName.FirstTouch],
            Tackling = values[(int)AttributeName.Tackling],
            Marking = values[(int)AttributeName.Marking],
            Heading = values[(int)AttributeName.Heading],
            Technique = values[(int)AttributeName.Technique],
            SetPieces = values[(int)AttributeName.SetPieces],
            Decisions = values[(int)AttributeName.Decisions],
            Vision = values[(int)AttributeName.Vision],
            Positioning = values[(int)AttributeName.Positioning],
            Composure = values[(int)AttributeName.Composure],
            Anticipation = values[(int)AttributeName.Anticipation],
            WorkRate = values[(int)AttributeName.WorkRate],
            Aggression = values[(int)AttributeName.Aggression],
            Leadership = values[(int)AttributeName.Leadership],
            Pace = values[(int)AttributeName.Pace],
            Acceleration = values[(int)AttributeName.Acceleration],
            Stamina = values[(int)AttributeName.Stamina],
            Strength = values[(int)AttributeName.Strength],
            Agility = values[(int)AttributeName.Agility],
            JumpingReach = values[(int)AttributeName.JumpingReach],
            Handling = values[(int)AttributeName.Handling],
            Reflexes = values[(int)AttributeName.Reflexes],
            OneOnOnes = values[(int)AttributeName.OneOnOnes],
            AerialAbility = values[(int)AttributeName.AerialAbility],
        };
    }
}

/// <summary>
/// One player's stored attributes, plus the schema version and checksum that make an accidental edit
/// detectable.
/// </summary>
/// <remarks>
/// Keyed by <see cref="PlayerId"/>: a player has exactly one attribute row (`data-model.md` §3.2). The
/// checksum covers the canonical attribute order, so a partial write or an out-of-band update that
/// leaves the values inconsistent with the digest is visible rather than silent.
/// </remarks>
public sealed class PlayerAttributes
{
    /// <summary>The attribute schema version a newly created row is stamped with.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerAttributes()
    {
    }

    /// <summary>Gets the owning player, which is also the primary key.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets finishing.</summary>
    public int Finishing { get; private set; }

    /// <summary>Gets passing.</summary>
    public int Passing { get; private set; }

    /// <summary>Gets crossing.</summary>
    public int Crossing { get; private set; }

    /// <summary>Gets dribbling.</summary>
    public int Dribbling { get; private set; }

    /// <summary>Gets first touch.</summary>
    public int FirstTouch { get; private set; }

    /// <summary>Gets tackling.</summary>
    public int Tackling { get; private set; }

    /// <summary>Gets marking.</summary>
    public int Marking { get; private set; }

    /// <summary>Gets heading.</summary>
    public int Heading { get; private set; }

    /// <summary>Gets technique.</summary>
    public int Technique { get; private set; }

    /// <summary>Gets set pieces.</summary>
    public int SetPieces { get; private set; }

    /// <summary>Gets decisions.</summary>
    public int Decisions { get; private set; }

    /// <summary>Gets vision.</summary>
    public int Vision { get; private set; }

    /// <summary>Gets positioning.</summary>
    public int Positioning { get; private set; }

    /// <summary>Gets composure.</summary>
    public int Composure { get; private set; }

    /// <summary>Gets anticipation.</summary>
    public int Anticipation { get; private set; }

    /// <summary>Gets work rate.</summary>
    public int WorkRate { get; private set; }

    /// <summary>Gets aggression.</summary>
    public int Aggression { get; private set; }

    /// <summary>Gets leadership.</summary>
    public int Leadership { get; private set; }

    /// <summary>Gets pace.</summary>
    public int Pace { get; private set; }

    /// <summary>Gets acceleration.</summary>
    public int Acceleration { get; private set; }

    /// <summary>Gets stamina.</summary>
    public int Stamina { get; private set; }

    /// <summary>Gets strength.</summary>
    public int Strength { get; private set; }

    /// <summary>Gets agility.</summary>
    public int Agility { get; private set; }

    /// <summary>Gets jumping reach.</summary>
    public int JumpingReach { get; private set; }

    /// <summary>Gets handling.</summary>
    public int Handling { get; private set; }

    /// <summary>Gets reflexes.</summary>
    public int Reflexes { get; private set; }

    /// <summary>Gets one-on-ones.</summary>
    public int OneOnOnes { get; private set; }

    /// <summary>Gets aerial ability.</summary>
    public int AerialAbility { get; private set; }

    /// <summary>Gets the attribute schema version the row was written with.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>Gets the checksum over the canonical attribute order.</summary>
    public string Checksum { get; private set; } = string.Empty;

    /// <summary>Creates a player's attributes, validating every value and stamping the checksum.</summary>
    /// <param name="playerId">The owning player.</param>
    /// <param name="attributes">The attribute set. Every value must be on the 1–20 scale (`TRN-4`).</param>
    public static PlayerAttributes Create(Guid playerId, PlayerAttributeSet attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.IsWithinScale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attributes),
                "Every attribute is between 1 and 20 (TRN-4).");
        }

        return new PlayerAttributes
        {
            PlayerId = playerId,
            Finishing = attributes.Finishing,
            Passing = attributes.Passing,
            Crossing = attributes.Crossing,
            Dribbling = attributes.Dribbling,
            FirstTouch = attributes.FirstTouch,
            Tackling = attributes.Tackling,
            Marking = attributes.Marking,
            Heading = attributes.Heading,
            Technique = attributes.Technique,
            SetPieces = attributes.SetPieces,
            Decisions = attributes.Decisions,
            Vision = attributes.Vision,
            Positioning = attributes.Positioning,
            Composure = attributes.Composure,
            Anticipation = attributes.Anticipation,
            WorkRate = attributes.WorkRate,
            Aggression = attributes.Aggression,
            Leadership = attributes.Leadership,
            Pace = attributes.Pace,
            Acceleration = attributes.Acceleration,
            Stamina = attributes.Stamina,
            Strength = attributes.Strength,
            Agility = attributes.Agility,
            JumpingReach = attributes.JumpingReach,
            Handling = attributes.Handling,
            Reflexes = attributes.Reflexes,
            OneOnOnes = attributes.OneOnOnes,
            AerialAbility = attributes.AerialAbility,
            SchemaVersion = CurrentSchemaVersion,
            Checksum = ChecksumOf(attributes),
        };
    }

    /// <summary>Projects the stored row back to an attribute set.</summary>
    public PlayerAttributeSet ToSet() => new()
    {
        Finishing = Finishing,
        Passing = Passing,
        Crossing = Crossing,
        Dribbling = Dribbling,
        FirstTouch = FirstTouch,
        Tackling = Tackling,
        Marking = Marking,
        Heading = Heading,
        Technique = Technique,
        SetPieces = SetPieces,
        Decisions = Decisions,
        Vision = Vision,
        Positioning = Positioning,
        Composure = Composure,
        Anticipation = Anticipation,
        WorkRate = WorkRate,
        Aggression = Aggression,
        Leadership = Leadership,
        Pace = Pace,
        Acceleration = Acceleration,
        Stamina = Stamina,
        Strength = Strength,
        Agility = Agility,
        JumpingReach = JumpingReach,
        Handling = Handling,
        Reflexes = Reflexes,
        OneOnOnes = OneOnOnes,
        AerialAbility = AerialAbility,
    };

    /// <summary>Gets whether the stored checksum still matches the stored values.</summary>
    public bool ChecksumMatches() => string.Equals(Checksum, ChecksumOf(ToSet()), StringComparison.Ordinal);

    private static string ChecksumOf(PlayerAttributeSet attributes) =>
        DeterministicDigest.Of(
        [
            .. attributes.Values.Select(value => value.ToString(CultureInfo.InvariantCulture)),
            CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
        ]);
}

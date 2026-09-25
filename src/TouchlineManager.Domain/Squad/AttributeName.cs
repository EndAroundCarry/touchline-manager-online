namespace TouchlineManager.Domain.Squad;

/// <summary>
/// The canonical identity of one attribute, in the fixed order every attribute table uses.
/// </summary>
/// <remarks>
/// The values are the canonical indices behind <see cref="PlayerAttributeSet.Values"/> and the checksum,
/// so a profile can name an attribute without positional indexing. Reordering or inserting a member is a
/// schema change, not a refactor.
/// </remarks>
public enum AttributeName
{
    /// <summary>Finishing (technical).</summary>
    Finishing = 0,

    /// <summary>Passing (technical).</summary>
    Passing = 1,

    /// <summary>Crossing (technical).</summary>
    Crossing = 2,

    /// <summary>Dribbling (technical).</summary>
    Dribbling = 3,

    /// <summary>First touch (technical).</summary>
    FirstTouch = 4,

    /// <summary>Tackling (technical).</summary>
    Tackling = 5,

    /// <summary>Marking (technical).</summary>
    Marking = 6,

    /// <summary>Heading (technical).</summary>
    Heading = 7,

    /// <summary>Technique (technical).</summary>
    Technique = 8,

    /// <summary>Set pieces (technical).</summary>
    SetPieces = 9,

    /// <summary>Decisions (mental).</summary>
    Decisions = 10,

    /// <summary>Vision (mental).</summary>
    Vision = 11,

    /// <summary>Positioning (mental).</summary>
    Positioning = 12,

    /// <summary>Composure (mental).</summary>
    Composure = 13,

    /// <summary>Anticipation (mental).</summary>
    Anticipation = 14,

    /// <summary>Work rate (mental).</summary>
    WorkRate = 15,

    /// <summary>Aggression (mental).</summary>
    Aggression = 16,

    /// <summary>Leadership (mental).</summary>
    Leadership = 17,

    /// <summary>Pace (physical).</summary>
    Pace = 18,

    /// <summary>Acceleration (physical).</summary>
    Acceleration = 19,

    /// <summary>Stamina (physical).</summary>
    Stamina = 20,

    /// <summary>Strength (physical).</summary>
    Strength = 21,

    /// <summary>Agility (physical).</summary>
    Agility = 22,

    /// <summary>Jumping reach (physical).</summary>
    JumpingReach = 23,

    /// <summary>Handling (goalkeeping).</summary>
    Handling = 24,

    /// <summary>Reflexes (goalkeeping).</summary>
    Reflexes = 25,

    /// <summary>One-on-ones (goalkeeping).</summary>
    OneOnOnes = 26,

    /// <summary>Aerial ability (goalkeeping).</summary>
    AerialAbility = 27,
}

/// <summary>The canonical attribute table: order, count, and family membership.</summary>
public static class AttributeNames
{
    /// <summary>How many attributes a player has.</summary>
    public const int Count = 28;

    /// <summary>Every attribute, in canonical order.</summary>
    public static readonly IReadOnlyList<AttributeName> All = [.. Enum.GetValues<AttributeName>()];

    /// <summary>Gets the family an attribute belongs to (`TRN-2`).</summary>
    /// <param name="name">The attribute.</param>
    public static AttributeFamily FamilyOf(AttributeName name) => name switch
    {
        AttributeName.Finishing
            or AttributeName.Passing
            or AttributeName.Crossing
            or AttributeName.Dribbling
            or AttributeName.FirstTouch
            or AttributeName.Tackling
            or AttributeName.Marking
            or AttributeName.Heading
            or AttributeName.Technique
            or AttributeName.SetPieces => AttributeFamily.Technical,
        AttributeName.Decisions
            or AttributeName.Vision
            or AttributeName.Positioning
            or AttributeName.Composure
            or AttributeName.Anticipation
            or AttributeName.WorkRate
            or AttributeName.Aggression
            or AttributeName.Leadership => AttributeFamily.Mental,
        AttributeName.Pace
            or AttributeName.Acceleration
            or AttributeName.Stamina
            or AttributeName.Strength
            or AttributeName.Agility
            or AttributeName.JumpingReach => AttributeFamily.Physical,
        AttributeName.Handling
            or AttributeName.Reflexes
            or AttributeName.OneOnOnes
            or AttributeName.AerialAbility => AttributeFamily.Goalkeeping,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown attribute."),
    };
}

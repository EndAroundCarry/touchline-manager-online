namespace TouchlineManager.MatchEngine.Model;

/// <summary>
/// One of a player's twenty-eight attributes, in the canonical order the attribute table uses.
/// </summary>
/// <remarks>
/// <para>
/// The engine defines its own vocabulary rather than reusing <c>TouchlineManager.Domain</c>'s enums: the
/// engine may not depend on the domain (DEP-2, ADR-0004), because a match's reproducibility is a
/// separate versioned contract from a generated world's. The numeric values deliberately match
/// <c>AttributeName</c>'s, and the application layer maps between them by value, so the order here is
/// part of the contract and reordering these members is a schema change rather than a refactor.
/// </para>
/// <para>
/// The same is true of <see cref="MatchPosition"/>, <see cref="MatchPositionFamily"/>,
/// <see cref="MatchRole"/>, and the eight instruction enums: they mirror the domain's, and their values
/// are what the snapshot's canonical hash is taken over.
/// </para>
/// </remarks>
public enum MatchAttributeName
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

/// <summary>The canonical attribute table: how many there are and which family each belongs to.</summary>
public static class MatchAttributeNames
{
    /// <summary>How many attributes a player has.</summary>
    public const int Count = 28;

    /// <summary>The lowest a stored attribute can be (`TRN-4`).</summary>
    public const int Min = 1;

    /// <summary>The highest a stored attribute can be (`TRN-4`).</summary>
    public const int Max = 20;

    /// <summary>Every attribute, in canonical order.</summary>
    public static readonly IReadOnlyList<MatchAttributeName> All = [.. Enum.GetValues<MatchAttributeName>()];

    /// <summary>The attributes that make up each unit-rating input, in canonical order.</summary>
    /// <param name="name">The attribute.</param>
    public static MatchAttributeFamily FamilyOf(MatchAttributeName name) => name switch
    {
        MatchAttributeName.Finishing
            or MatchAttributeName.Passing
            or MatchAttributeName.Crossing
            or MatchAttributeName.Dribbling
            or MatchAttributeName.FirstTouch
            or MatchAttributeName.Tackling
            or MatchAttributeName.Marking
            or MatchAttributeName.Heading
            or MatchAttributeName.Technique
            or MatchAttributeName.SetPieces => MatchAttributeFamily.Technical,
        MatchAttributeName.Decisions
            or MatchAttributeName.Vision
            or MatchAttributeName.Positioning
            or MatchAttributeName.Composure
            or MatchAttributeName.Anticipation
            or MatchAttributeName.WorkRate
            or MatchAttributeName.Aggression
            or MatchAttributeName.Leadership => MatchAttributeFamily.Mental,
        MatchAttributeName.Pace
            or MatchAttributeName.Acceleration
            or MatchAttributeName.Stamina
            or MatchAttributeName.Strength
            or MatchAttributeName.Agility
            or MatchAttributeName.JumpingReach => MatchAttributeFamily.Physical,
        MatchAttributeName.Handling
            or MatchAttributeName.Reflexes
            or MatchAttributeName.OneOnOnes
            or MatchAttributeName.AerialAbility => MatchAttributeFamily.Goalkeeping,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown attribute."),
    };
}

/// <summary>The band an attribute belongs to, mirroring the domain's attribute families.</summary>
public enum MatchAttributeFamily
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

/// <summary>The specific position a player is most at home in.</summary>
public enum MatchPosition
{
    /// <summary>Goalkeeper.</summary>
    Goalkeeper = 0,

    /// <summary>Right back.</summary>
    RightBack = 1,

    /// <summary>Centre back.</summary>
    CentreBack = 2,

    /// <summary>Left back.</summary>
    LeftBack = 3,

    /// <summary>Defensive midfielder.</summary>
    DefensiveMidfielder = 4,

    /// <summary>Central midfielder.</summary>
    CentralMidfielder = 5,

    /// <summary>Attacking midfielder.</summary>
    AttackingMidfielder = 6,

    /// <summary>Right winger.</summary>
    RightWinger = 7,

    /// <summary>Left winger.</summary>
    LeftWinger = 8,

    /// <summary>Striker.</summary>
    Striker = 9,
}

/// <summary>The broad band a position belongs to.</summary>
public enum MatchPositionFamily
{
    /// <summary>Goalkeeper.</summary>
    Goalkeeper = 0,

    /// <summary>Defence.</summary>
    Defence = 1,

    /// <summary>Midfield.</summary>
    Midfield = 2,

    /// <summary>Attack.</summary>
    Attack = 3,
}

/// <summary>The job a slot asks its occupant to do (`TAC-8`).</summary>
public enum MatchRole
{
    /// <summary>Shot-stopping goalkeeper.</summary>
    Goalkeeper = 0,

    /// <summary>Stays in the defensive line.</summary>
    CentreBack = 1,

    /// <summary>Defends the flank from a back four.</summary>
    FullBack = 2,

    /// <summary>Defends the flank with licence to attack.</summary>
    WingBack = 3,

    /// <summary>Screens the defence.</summary>
    DefensiveMidfielder = 4,

    /// <summary>Links defence and attack.</summary>
    CentralMidfielder = 5,

    /// <summary>Plays between the lines.</summary>
    AttackingMidfielder = 6,

    /// <summary>Attacks from a wide starting position.</summary>
    Winger = 7,

    /// <summary>Leads the line.</summary>
    Striker = 8,
}

/// <summary>The team's overall approach (`INS-1`).</summary>
public enum MatchMentality
{
    /// <summary>Sit deep and protect.</summary>
    Defensive = 0,

    /// <summary>Stay compact and take few risks.</summary>
    Cautious = 1,

    /// <summary>Neither instruct nor inhibit.</summary>
    Balanced = 2,

    /// <summary>Commit players forward.</summary>
    Positive = 3,

    /// <summary>Chase the game.</summary>
    Attacking = 4,
}

/// <summary>How quickly the team moves the ball (`INS-2`).</summary>
public enum MatchTempo
{
    /// <summary>Slow and deliberate.</summary>
    Low = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    Normal = 1,

    /// <summary>Fast and direct in transition.</summary>
    High = 2,
}

/// <summary>What kind of pass the team favours (`INS-3`).</summary>
public enum MatchPassingStyle
{
    /// <summary>Short passes into feet.</summary>
    ShortPassing = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    MixedPassing = 1,

    /// <summary>Long, forward passes.</summary>
    DirectPassing = 2,
}

/// <summary>How far the team spreads across the pitch (`INS-4`).</summary>
public enum MatchWidth
{
    /// <summary>Cramped, congesting the middle.</summary>
    Narrow = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    Normal = 1,

    /// <summary>Stretched, using the full pitch.</summary>
    Wide = 2,
}

/// <summary>From where the team begins to press (`INS-5`).</summary>
public enum MatchPressing
{
    /// <summary>Defend from a low block.</summary>
    LowBlock = 0,

    /// <summary>Defend from a mid block.</summary>
    MidBlock = 1,

    /// <summary>Press high up the pitch.</summary>
    HighPress = 2,
}

/// <summary>How high the defensive line holds (`INS-6`).</summary>
public enum MatchDefensiveLine
{
    /// <summary>Drop off.</summary>
    Deep = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    Normal = 1,

    /// <summary>Hold a high line.</summary>
    High = 2,
}

/// <summary>How committed the tackling is (`INS-7`).</summary>
public enum MatchTacklingStyle
{
    /// <summary>Jockey and delay.</summary>
    StayOnFeet = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    Normal = 1,

    /// <summary>Commit to the challenge, risking cards.</summary>
    Aggressive = 2,
}

/// <summary>Whether the team runs down the clock (`INS-8`).</summary>
public enum MatchTimeWasting
{
    /// <summary>Never.</summary>
    Off = 0,

    /// <summary>Only when leading late.</summary>
    Situational = 1,

    /// <summary>Whenever possible.</summary>
    On = 2,
}

/// <summary>Which of the two sides an event, statistic, or fact belongs to.</summary>
public enum MatchSide
{
    /// <summary>The home side.</summary>
    Home = 0,

    /// <summary>The away side.</summary>
    Away = 1,
}

/// <summary>Where on the pitch a shot was taken from.</summary>
public enum ShotZone
{
    /// <summary>Straight in front of goal.</summary>
    Central = 0,

    /// <summary>Left of centre, inside the width of the box.</summary>
    InsideLeft = 1,

    /// <summary>Right of centre, inside the width of the box.</summary>
    InsideRight = 2,

    /// <summary>Wide on the left.</summary>
    WideLeft = 3,

    /// <summary>Wide on the right.</summary>
    WideRight = 4,
}

/// <summary>Stable codes and relationships for the engine's vocabulary.</summary>
/// <remarks>
/// The codes are the transport form of a value in an event payload or a commentary token, and are part of
/// the engine's contract because they appear in canonical snapshots and stored tokens, so renaming one is
/// an engine-version change. The relationships are what slot validation, role familiarity, and the rating
/// weights all agree on.
/// </remarks>
public static class MatchVocabulary
{
    /// <summary>Converts a shot zone to its stable code.</summary>
    /// <param name="zone">The zone.</param>
    public static string Code(this ShotZone zone) => zone switch
    {
        ShotZone.Central => "central",
        ShotZone.InsideLeft => "inside_left",
        ShotZone.InsideRight => "inside_right",
        ShotZone.WideLeft => "wide_left",
        ShotZone.WideRight => "wide_right",
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unknown shot zone."),
    };

    /// <summary>Converts a side to its stable code.</summary>
    /// <param name="side">The side.</param>
    public static string Code(this MatchSide side) => side switch
    {
        MatchSide.Home => "home",
        MatchSide.Away => "away",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown side."),
    };

    /// <summary>Gets the position family a position belongs to.</summary>
    /// <param name="position">The position.</param>
    public static MatchPositionFamily FamilyOf(this MatchPosition position) => position switch
    {
        MatchPosition.Goalkeeper => MatchPositionFamily.Goalkeeper,
        MatchPosition.RightBack or MatchPosition.CentreBack or MatchPosition.LeftBack =>
            MatchPositionFamily.Defence,
        MatchPosition.DefensiveMidfielder
            or MatchPosition.CentralMidfielder
            or MatchPosition.AttackingMidfielder => MatchPositionFamily.Midfield,
        MatchPosition.RightWinger or MatchPosition.LeftWinger or MatchPosition.Striker =>
            MatchPositionFamily.Attack,
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown position."),
    };

    /// <summary>Gets the position family a role belongs to.</summary>
    /// <param name="role">The role.</param>
    public static MatchPositionFamily FamilyOf(this MatchRole role) => role switch
    {
        MatchRole.Goalkeeper => MatchPositionFamily.Goalkeeper,
        MatchRole.CentreBack or MatchRole.FullBack or MatchRole.WingBack => MatchPositionFamily.Defence,
        MatchRole.DefensiveMidfielder
            or MatchRole.CentralMidfielder
            or MatchRole.AttackingMidfielder => MatchPositionFamily.Midfield,
        MatchRole.Winger or MatchRole.Striker => MatchPositionFamily.Attack,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}

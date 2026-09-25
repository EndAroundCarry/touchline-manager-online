namespace TouchlineManager.Domain.Squad;

/// <summary>
/// The specific position a player is most at home in (master plan §6.5).
/// </summary>
/// <remarks>
/// The ten values are the ones a formation slot can name. A player also carries secondary positions, so
/// one primary value plus the secondaries is what the selection validator compares against a slot's
/// <see cref="PositionFamily"/> to apply the out-of-position familiarity penalty (`INS-10`).
/// </remarks>
public enum PlayerPosition
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

/// <summary>
/// The broad band a position belongs to.
/// </summary>
/// <remarks>
/// Quotas, familiarity, and the selection validator all work at this granularity rather than at the
/// ten-position grain, so a formation can be checked without hard-coding each preset's exact shape.
/// </remarks>
public enum PositionFamily
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

/// <summary>Stable codes and storage representation for <see cref="PlayerPosition"/>.</summary>
public static class PlayerPositions
{
    /// <summary>The code for <see cref="PlayerPosition.Goalkeeper"/>.</summary>
    public const string GoalkeeperCode = "gk";

    /// <summary>The code for <see cref="PlayerPosition.RightBack"/>.</summary>
    public const string RightBackCode = "rb";

    /// <summary>The code for <see cref="PlayerPosition.CentreBack"/>.</summary>
    public const string CentreBackCode = "cb";

    /// <summary>The code for <see cref="PlayerPosition.LeftBack"/>.</summary>
    public const string LeftBackCode = "lb";

    /// <summary>The code for <see cref="PlayerPosition.DefensiveMidfielder"/>.</summary>
    public const string DefensiveMidfielderCode = "dm";

    /// <summary>The code for <see cref="PlayerPosition.CentralMidfielder"/>.</summary>
    public const string CentralMidfielderCode = "cm";

    /// <summary>The code for <see cref="PlayerPosition.AttackingMidfielder"/>.</summary>
    public const string AttackingMidfielderCode = "am";

    /// <summary>The code for <see cref="PlayerPosition.RightWinger"/>.</summary>
    public const string RightWingerCode = "rw";

    /// <summary>The code for <see cref="PlayerPosition.LeftWinger"/>.</summary>
    public const string LeftWingerCode = "lw";

    /// <summary>The code for <see cref="PlayerPosition.Striker"/>.</summary>
    public const string StrikerCode = "st";

    /// <summary>Every position, in declaration order.</summary>
    public static readonly IReadOnlyList<PlayerPosition> All =
    [
        PlayerPosition.Goalkeeper,
        PlayerPosition.RightBack,
        PlayerPosition.CentreBack,
        PlayerPosition.LeftBack,
        PlayerPosition.DefensiveMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.AttackingMidfielder,
        PlayerPosition.RightWinger,
        PlayerPosition.LeftWinger,
        PlayerPosition.Striker,
    ];

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 2;

    /// <summary>Converts a position to its stable code.</summary>
    /// <param name="position">The position.</param>
    public static string ToCode(this PlayerPosition position) => position switch
    {
        PlayerPosition.Goalkeeper => GoalkeeperCode,
        PlayerPosition.RightBack => RightBackCode,
        PlayerPosition.CentreBack => CentreBackCode,
        PlayerPosition.LeftBack => LeftBackCode,
        PlayerPosition.DefensiveMidfielder => DefensiveMidfielderCode,
        PlayerPosition.CentralMidfielder => CentralMidfielderCode,
        PlayerPosition.AttackingMidfielder => AttackingMidfielderCode,
        PlayerPosition.RightWinger => RightWingerCode,
        PlayerPosition.LeftWinger => LeftWingerCode,
        PlayerPosition.Striker => StrikerCode,
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown position."),
    };

    /// <summary>Parses a stable code back to its position.</summary>
    /// <param name="code">The stable code.</param>
    public static PlayerPosition FromCode(string code) => code switch
    {
        GoalkeeperCode => PlayerPosition.Goalkeeper,
        RightBackCode => PlayerPosition.RightBack,
        CentreBackCode => PlayerPosition.CentreBack,
        LeftBackCode => PlayerPosition.LeftBack,
        DefensiveMidfielderCode => PlayerPosition.DefensiveMidfielder,
        CentralMidfielderCode => PlayerPosition.CentralMidfielder,
        AttackingMidfielderCode => PlayerPosition.AttackingMidfielder,
        RightWingerCode => PlayerPosition.RightWinger,
        LeftWingerCode => PlayerPosition.LeftWinger,
        StrikerCode => PlayerPosition.Striker,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown position code."),
    };

    /// <summary>Gets the family a position belongs to.</summary>
    /// <param name="position">The position.</param>
    public static PositionFamily FamilyOf(PlayerPosition position) => position switch
    {
        PlayerPosition.Goalkeeper => PositionFamily.Goalkeeper,
        PlayerPosition.RightBack or PlayerPosition.CentreBack or PlayerPosition.LeftBack =>
            PositionFamily.Defence,
        PlayerPosition.DefensiveMidfielder
            or PlayerPosition.CentralMidfielder
            or PlayerPosition.AttackingMidfielder => PositionFamily.Midfield,
        PlayerPosition.RightWinger or PlayerPosition.LeftWinger or PlayerPosition.Striker =>
            PositionFamily.Attack,
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown position."),
    };

    /// <summary>
    /// Renders a set of positions as a stable, comma-separated code list.
    /// </summary>
    /// <remarks>
    /// Ordered by enum value rather than by input order, so the stored form of a player's secondary
    /// positions is a pure function of the set and a regenerated squad compares equal row for row.
    /// </remarks>
    /// <param name="positions">The positions.</param>
    public static string JoinCodes(IEnumerable<PlayerPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        return string.Join(',', positions.Distinct().OrderBy(position => position).Select(ToCode));
    }

    /// <summary>Parses a comma-separated code list back to positions. An empty string is no positions.</summary>
    /// <param name="codes">The stored code list.</param>
    public static IReadOnlyList<PlayerPosition> ParseCodes(string codes)
    {
        ArgumentNullException.ThrowIfNull(codes);

        if (string.IsNullOrWhiteSpace(codes))
        {
            return [];
        }

        return [.. codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(FromCode)];
    }
}

/// <summary>Stable codes and storage representation for <see cref="PositionFamily"/>.</summary>
public static class PositionFamilies
{
    /// <summary>The code for <see cref="PositionFamily.Goalkeeper"/>.</summary>
    public const string GoalkeeperCode = "goalkeeper";

    /// <summary>The code for <see cref="PositionFamily.Defence"/>.</summary>
    public const string DefenceCode = "defence";

    /// <summary>The code for <see cref="PositionFamily.Midfield"/>.</summary>
    public const string MidfieldCode = "midfield";

    /// <summary>The code for <see cref="PositionFamily.Attack"/>.</summary>
    public const string AttackCode = "attack";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 10;

    /// <summary>Converts a family to its stable code.</summary>
    /// <param name="family">The family.</param>
    public static string ToCode(this PositionFamily family) => family switch
    {
        PositionFamily.Goalkeeper => GoalkeeperCode,
        PositionFamily.Defence => DefenceCode,
        PositionFamily.Midfield => MidfieldCode,
        PositionFamily.Attack => AttackCode,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown position family."),
    };

    /// <summary>Parses a stable code back to its family.</summary>
    /// <param name="code">The stable code.</param>
    public static PositionFamily FromCode(string code) => code switch
    {
        GoalkeeperCode => PositionFamily.Goalkeeper,
        DefenceCode => PositionFamily.Defence,
        MidfieldCode => PositionFamily.Midfield,
        AttackCode => PositionFamily.Attack,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown position family code."),
    };
}

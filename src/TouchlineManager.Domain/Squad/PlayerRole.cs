namespace TouchlineManager.Domain.Squad;

/// <summary>
/// The job a slot asks its occupant to do (`TAC-8`).
/// </summary>
/// <remarks>
/// A role is narrower than a <see cref="PositionFamily"/> and is what the manager actually picks: a
/// four-four-two names two centre backs, but a slot may ask one of them to step out with the ball. The
/// engine's role effects arrive with the engine in Stage 5; Stage 4 stores and validates the choice.
/// </remarks>
public enum PlayerRole
{
    /// <summary>Shot-stopping goalkeeper.</summary>
    Goalkeeper = 0,

    /// <summary>Stays in the defensive line.</summary>
    CentreBack = 1,

    /// <summary>Defends the flank from a back four.</summary>
    FullBack = 2,

    /// <summary>Defends the flank with licence to attack from a back three or five.</summary>
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

/// <summary>Stable codes and storage representation for <see cref="PlayerRole"/>.</summary>
public static class PlayerRoles
{
    /// <summary>The code for <see cref="PlayerRole.Goalkeeper"/>.</summary>
    public const string GoalkeeperCode = "goalkeeper";

    /// <summary>The code for <see cref="PlayerRole.CentreBack"/>.</summary>
    public const string CentreBackCode = "centre_back";

    /// <summary>The code for <see cref="PlayerRole.FullBack"/>.</summary>
    public const string FullBackCode = "full_back";

    /// <summary>The code for <see cref="PlayerRole.WingBack"/>.</summary>
    public const string WingBackCode = "wing_back";

    /// <summary>The code for <see cref="PlayerRole.DefensiveMidfielder"/>.</summary>
    public const string DefensiveMidfielderCode = "defensive_midfielder";

    /// <summary>The code for <see cref="PlayerRole.CentralMidfielder"/>.</summary>
    public const string CentralMidfielderCode = "central_midfielder";

    /// <summary>The code for <see cref="PlayerRole.AttackingMidfielder"/>.</summary>
    public const string AttackingMidfielderCode = "attacking_midfielder";

    /// <summary>The code for <see cref="PlayerRole.Winger"/>.</summary>
    public const string WingerCode = "winger";

    /// <summary>The code for <see cref="PlayerRole.Striker"/>.</summary>
    public const string StrikerCode = "striker";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 21;

    /// <summary>Converts a role to its stable code.</summary>
    /// <param name="role">The role.</param>
    public static string ToCode(this PlayerRole role) => role switch
    {
        PlayerRole.Goalkeeper => GoalkeeperCode,
        PlayerRole.CentreBack => CentreBackCode,
        PlayerRole.FullBack => FullBackCode,
        PlayerRole.WingBack => WingBackCode,
        PlayerRole.DefensiveMidfielder => DefensiveMidfielderCode,
        PlayerRole.CentralMidfielder => CentralMidfielderCode,
        PlayerRole.AttackingMidfielder => AttackingMidfielderCode,
        PlayerRole.Winger => WingerCode,
        PlayerRole.Striker => StrikerCode,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    /// <summary>Parses a stable code back to its role.</summary>
    /// <param name="code">The stable code.</param>
    public static PlayerRole FromCode(string code) => code switch
    {
        GoalkeeperCode => PlayerRole.Goalkeeper,
        CentreBackCode => PlayerRole.CentreBack,
        FullBackCode => PlayerRole.FullBack,
        WingBackCode => PlayerRole.WingBack,
        DefensiveMidfielderCode => PlayerRole.DefensiveMidfielder,
        CentralMidfielderCode => PlayerRole.CentralMidfielder,
        AttackingMidfielderCode => PlayerRole.AttackingMidfielder,
        WingerCode => PlayerRole.Winger,
        StrikerCode => PlayerRole.Striker,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown role code."),
    };

    /// <summary>Gets the position family a role belongs to. A slot's role and family must agree (`TAC-8`).</summary>
    /// <param name="role">The role.</param>
    public static PositionFamily FamilyOf(PlayerRole role) => role switch
    {
        PlayerRole.Goalkeeper => PositionFamily.Goalkeeper,
        PlayerRole.CentreBack or PlayerRole.FullBack or PlayerRole.WingBack => PositionFamily.Defence,
        PlayerRole.DefensiveMidfielder
            or PlayerRole.CentralMidfielder
            or PlayerRole.AttackingMidfielder => PositionFamily.Midfield,
        PlayerRole.Winger or PlayerRole.Striker => PositionFamily.Attack,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}

namespace TouchlineManager.Application.Squad;

/// <summary>
/// Why a squad read succeeded or was refused.
/// </summary>
/// <remarks>
/// One vocabulary for the three reads rather than three near-identical enums, because they refuse for the
/// same reasons and the API maps them the same way. The three authorization refusals are carried straight
/// through from <see cref="ClubAccessOutcome"/>; <see cref="PlayerNotFound"/> is only ever produced by the
/// player read.
/// </remarks>
public enum SquadReadOutcome
{
    /// <summary>The read succeeded.</summary>
    Found = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 2,

    /// <summary>The manager holds no club.</summary>
    NoClub = 3,

    /// <summary>The manager holds a club, but not the one asked about.</summary>
    ClubNotManaged = 4,

    /// <summary>No club exists with the requested identity.</summary>
    ClubNotFound = 5,

    /// <summary>No player exists with the requested identity.</summary>
    PlayerNotFound = 6,
}

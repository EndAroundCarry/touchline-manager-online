namespace TouchlineManager.Application.Competition;

/// <summary>
/// Why a competition read succeeded or was refused.
/// </summary>
/// <remarks>
/// One vocabulary for the fixture reads and the prepare-match read rather than four near-identical enums,
/// because they refuse for the same reasons and the API maps them the same way. The authorization refusals
/// are carried straight through from <see cref="Squad.ClubAccessOutcome"/>; <see cref="FixtureNotFound"/>
/// and <see cref="DivisionNotFound"/> are only ever produced by the reads that can look an identity up.
/// </remarks>
public enum CompetitionReadOutcome
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

    /// <summary>No fixture exists with the requested identity.</summary>
    FixtureNotFound = 6,

    /// <summary>No division exists with the requested identity, or it has no season in progress.</summary>
    DivisionNotFound = 7,
}

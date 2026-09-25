using TouchlineManager.Application.Abstractions.World;

namespace TouchlineManager.Application.Squad;

/// <summary>Whether a manager may read the club they asked about (master plan §10.9).</summary>
public enum ClubAccessOutcome
{
    /// <summary>The caller holds the club, so the read may proceed.</summary>
    Granted = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 2,

    /// <summary>The manager holds no club.</summary>
    NoClub = 3,

    /// <summary>The manager holds a club, but not this one.</summary>
    ClubNotManaged = 4,

    /// <summary>No club exists with the requested identity.</summary>
    ClubNotFound = 5,
}

/// <summary>The verdict, and the club the caller actually holds.</summary>
/// <param name="Outcome">The verdict.</param>
/// <param name="ClubId">The caller's club, or <see cref="Guid.Empty"/> when access was not granted.</param>
public sealed record ClubAccessResult(ClubAccessOutcome Outcome, Guid ClubId);

/// <summary>
/// Decides whether the authenticated manager may read a club's squad data (master plan §10.9, §15.4).
/// </summary>
/// <remarks>
/// <para>
/// One shared step rather than three copies, because the squad, player, and contract reads refuse for
/// exactly the same reasons. The squad list carries player condition and the contract list carries wages,
/// and `data-classification.md` classes both as C1 — readable where the viewer is authorized, which for
/// club state means the manager who holds it.
/// </para>
/// <para>
/// A club the caller does not hold is refused whether or not it exists is checked separately, and in this
/// order: the manager's own tenure is the precondition, then the requested club's existence, then the
/// match. That way "you manage no club" and "no such club" stay distinguishable from "not yours", which
/// is what §15.4's own-club/other-club test matrix is checking for.
/// </para>
/// </remarks>
public sealed class ResolveOwnedClub
{
    private readonly IWorldRepository _world;
    private readonly IManagerRepository _managers;
    private readonly IClubTenureRepository _tenures;
    private readonly IClubRepository _clubs;

    /// <summary>Initializes the resolver.</summary>
    public ResolveOwnedClub(
        IWorldRepository world,
        IManagerRepository managers,
        IClubTenureRepository tenures,
        IClubRepository clubs)
    {
        _world = world;
        _managers = managers;
        _tenures = tenures;
        _clubs = clubs;
    }

    /// <summary>
    /// Resolves the manager's club, optionally requiring it to be a specific one.
    /// </summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="clubId">The club being asked about, or null to resolve whichever club the manager holds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ClubAccessResult> ExecuteAsync(
        Guid userId,
        Guid? clubId,
        CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return Refused(ClubAccessOutcome.WorldNotSeeded);
        }

        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return Refused(ClubAccessOutcome.NoManagerProfile);
        }

        var tenure = await _tenures.FindOpenByManagerAsync(manager.Id, cancellationToken);

        if (tenure is null)
        {
            return Refused(ClubAccessOutcome.NoClub);
        }

        if (clubId is null)
        {
            return new ClubAccessResult(ClubAccessOutcome.Granted, tenure.ClubId);
        }

        // Existence before ownership, so a nonexistent club is a 404 rather than a 403. Club identity is
        // public game data (C0), so saying a club does not exist discloses nothing.
        var club = await _clubs.FindAsync(clubId.Value, cancellationToken);

        if (club is null)
        {
            return Refused(ClubAccessOutcome.ClubNotFound);
        }

        return tenure.ClubId == clubId.Value
            ? new ClubAccessResult(ClubAccessOutcome.Granted, tenure.ClubId)
            : Refused(ClubAccessOutcome.ClubNotManaged);
    }

    private static ClubAccessResult Refused(ClubAccessOutcome outcome) =>
        new(outcome, Guid.Empty);
}

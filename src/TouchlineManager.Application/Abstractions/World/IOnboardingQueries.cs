using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Abstractions.World;

/// <summary>
/// Identifies one advisory lock: a scope and the row the scope applies to.
/// </summary>
/// <remarks>
/// Two scopes are needed, not one. A country lock serialises pyramid expansion so two takeovers cannot
/// each decide to create the next tier (`PYR-3`); a manager lock serialises one person's claims so a
/// manager cannot end up with two clubs by racing themselves from two devices. Both are taken in the
/// same order by every caller, which is what keeps the pair deadlock-free.
/// </remarks>
/// <param name="Scope">The kind of row being locked.</param>
/// <param name="Target">The identity of the row.</param>
public readonly record struct AdvisoryLockKey(string Scope, Guid Target)
{
    /// <summary>A country-scoped lock, used for pyramid expansion.</summary>
    public static AdvisoryLockKey Country(Guid countryId) => new("country", countryId);

    /// <summary>A manager-scoped lock, used for club claims.</summary>
    public static AdvisoryLockKey Manager(Guid managerId) => new("manager", managerId);

    /// <summary>Renders the lock for diagnostics. Never contains anything sensitive.</summary>
    public override string ToString() => $"{Scope}:{Target}";
}

/// <summary>
/// A transaction-scoped advisory lock, used to serialise the few workflows that must not interleave.
/// </summary>
/// <remarks>
/// <para>
/// Ordinary concurrency is handled by row leases, optimistic versions, and unique indexes. This exists for
/// the cases where a decision is read-then-write across rows: two concurrent takeovers can each read "the
/// tier is now full" and each insert a provisioning request, and the unique index on
/// <c>(country_id, target_tier)</c> only stops the second one after it has already done its work
/// (`PYR-3`, ADR-0005). Holding a lock around the evaluation means the second arrival sees the first
/// one's request and gives the same answer instead of failing on a constraint.
/// </para>
/// <para>
/// The lock is released when the transaction ends, which is why this must be called inside one: an
/// "acquire and forget" call would hold the lock for the life of the pooled connection.
/// </para>
/// </remarks>
public interface IAdvisoryLock
{
    /// <summary>Blocks until this transaction holds the lock.</summary>
    /// <param name="key">The lock to take.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AcquireAsync(AdvisoryLockKey key, CancellationToken cancellationToken);
}

/// <summary>How full a country's pyramid is, as measured from the database (`WORLD-8`, `PYR-1`).</summary>
/// <param name="CountryId">The country.</param>
/// <param name="LowestActiveTier">The tier a new manager may join.</param>
/// <param name="DivisionId">That tier's division row.</param>
/// <param name="DivisionSeasonId">That tier's instance in the current season, which is what membership is measured against.</param>
/// <param name="DivisionName">That tier's generated name.</param>
/// <param name="ClubsInLowestTier">How many clubs the tier holds.</param>
/// <param name="HumanOccupiedClubs">
/// How many hold an open human tenure. Inactive tenures count (`OCC-8`); suspended accounts do not.
/// </param>
public sealed record CountryCapacitySnapshot(
    Guid CountryId,
    int LowestActiveTier,
    Guid DivisionId,
    Guid DivisionSeasonId,
    string DivisionName,
    int ClubsInLowestTier,
    int HumanOccupiedClubs);

/// <summary>One club in a country's lowest active tier, as offered to a manager.</summary>
/// <param name="Id">The club identity.</param>
/// <param name="Name">The generated name.</param>
/// <param name="ShortName">The abbreviation.</param>
/// <param name="City">The generated home city.</param>
/// <param name="Region">The generated region.</param>
/// <param name="BadgeSeed">The badge seed.</param>
/// <param name="Reputation">The club's reputation.</param>
/// <param name="StadiumBaseline">The fixed stadium baseline.</param>
/// <param name="IsAvailable">Whether the club is AI-controlled and therefore claimable.</param>
public sealed record AvailableClubRow(
    Guid Id,
    string Name,
    string ShortName,
    string City,
    string Region,
    string BadgeSeed,
    int Reputation,
    long StadiumBaseline,
    bool IsAvailable);

/// <summary>Everything the inherited-club dashboard reads (`WORLD-9`).</summary>
/// <param name="Club">The club.</param>
/// <param name="Country">The country the club plays in.</param>
/// <param name="Division">The tier the club plays in.</param>
/// <param name="Season">The season in progress.</param>
/// <param name="Tenure">The open tenure, or null when the club is AI-controlled.</param>
/// <param name="CashMinor">The club's cash, or null if no account exists yet.</param>
/// <param name="ReservedMinor">The club's reserved funds, or null if no account exists yet.</param>
public sealed record ClubDashboardSnapshot(
    Club Club,
    Country Country,
    Division Division,
    Season Season,
    ClubTenure? Tenure,
    long? CashMinor,
    long? ReservedMinor);

/// <summary>A manager's current club, with the context needed to name it.</summary>
/// <param name="Tenure">The open tenure.</param>
/// <param name="Club">The controlled club.</param>
/// <param name="Country">The club's country.</param>
/// <param name="Division">The club's tier.</param>
public sealed record ClubTenureSnapshot(
    ClubTenure Tenure,
    Club Club,
    Country Country,
    Division Division);

/// <summary>
/// The read side of onboarding.
/// </summary>
/// <remarks>
/// <para>
/// These are projections, not aggregates: each one is a single query shaped for one screen, and none of
/// them is used to make a decision. Keeping them off the write repositories means a screen can be changed
/// without widening what a command can reach (`MOD-3`).
/// </para>
/// <para>
/// Occupancy is counted by joining tenures to accounts and excluding suspended ones, which is a
/// cross-module read. It is a read, so it is allowed; the write that follows it happens through the
/// takeover use case.
/// </para>
/// </remarks>
public interface IOnboardingQueries
{
    /// <summary>Measures a country's lowest active tier, or returns null if it has no active division.</summary>
    Task<CountryCapacitySnapshot?> GetCountryCapacityAsync(Guid countryId, CancellationToken cancellationToken);

    /// <summary>Lists the clubs of a division-season with their availability, in a stable order.</summary>
    Task<IReadOnlyList<AvailableClubRow>> GetAvailableClubsAsync(
        Guid divisionSeasonId,
        CancellationToken cancellationToken);

    /// <summary>Reads everything the inherited-club dashboard shows, or returns null if the club is unknown.</summary>
    /// <param name="clubId">The club to read.</param>
    /// <param name="seasonNumber">The season whose placement to use, so a club read during rollover shows the season being played.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClubDashboardSnapshot?> GetClubDashboardAsync(
        Guid clubId,
        int seasonNumber,
        CancellationToken cancellationToken);

    /// <summary>Reads the manager's open tenure with its club context, or returns null.</summary>
    Task<ClubTenureSnapshot?> GetCurrentTenureAsync(Guid managerId, CancellationToken cancellationToken);
}

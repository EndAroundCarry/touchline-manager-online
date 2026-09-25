using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Abstractions.World;

/// <summary>
/// Persistence for club records.
/// </summary>
/// <remarks>
/// Deliberately tiny. A club is generated, never entered (`WORLD-3`), and control over it is expressed by
/// a tenure rather than a column (`WORLD-7`), so there is no update path here at all — the only
/// mutation a club ever sees is an audited administrative retirement.
/// </remarks>
public interface IClubRepository
{
    /// <summary>Finds a club by identity.</summary>
    Task<Club?> FindAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Stages a newly generated club.</summary>
    void Add(Club club);
}

/// <summary>Persistence for manager profiles (`WORLD-7`).</summary>
public interface IManagerRepository
{
    /// <summary>Finds the profile belonging to an account. One profile per account.</summary>
    Task<Manager?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Finds a profile by identity.</summary>
    Task<Manager?> FindByIdAsync(Guid managerId, CancellationToken cancellationToken);

    /// <summary>Stages a new profile.</summary>
    void Add(Manager manager);
}

/// <summary>
/// Persistence for club tenures, the whole of the ownership model (`WORLD-7`).
/// </summary>
/// <remarks>
/// "Open" always means <c>control_status &lt;&gt; 'closed'</c>, so an <c>inactive</c> tenure still counts
/// as occupying its club and its manager (`OCC-8`, `OCC-9`). Reading only <c>active</c> tenures here
/// would let a returning manager be treated as clubless and let a full tier look empty.
/// </remarks>
public interface IClubTenureRepository
{
    /// <summary>Finds the manager's open tenure, if any.</summary>
    Task<ClubTenure?> FindOpenByManagerAsync(Guid managerId, CancellationToken cancellationToken);

    /// <summary>Finds the club's open tenure, if any.</summary>
    Task<ClubTenure?> FindOpenByClubAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a tenure by the idempotency key that created it, so a retried takeover can answer with the
    /// outcome of the first attempt instead of attempting a second one (`CONC-3`).
    /// </summary>
    Task<ClubTenure?> FindByTakeoverKeyAsync(string takeoverIdempotencyKey, CancellationToken cancellationToken);

    /// <summary>Stages a new tenure.</summary>
    void Add(ClubTenure tenure);
}

/// <summary>Persistence for pyramid-expansion requests (`PYR-2`, `PYR-3`).</summary>
public interface IDivisionProvisioningRequestRepository
{
    /// <summary>Finds the request for a country's target tier, if one exists.</summary>
    Task<DivisionProvisioningRequest?> FindAsync(
        Guid countryId,
        int targetTier,
        CancellationToken cancellationToken);

    /// <summary>Stages a new request.</summary>
    void Add(DivisionProvisioningRequest request);
}

/// <summary>
/// Persistence for generation-run records, so every generated world is traceable to its inputs
/// (`PYR-14`).
/// </summary>
public interface IGenerationRunRepository
{
    /// <summary>Stages a generation-run record.</summary>
    void Add(GenerationRun run);
}

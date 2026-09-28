using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Market;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Reads and edits a manager's private shortlist (`SCT-3`).
/// </summary>
/// <remarks>
/// <para>
/// The shortlist belongs to the manager profile, not to the club, so it survives a change of club and is never
/// visible to another manager. That is why it is keyed by the manager identity the resolver reads from the
/// tenure rather than by anything the client sends.
/// </para>
/// <para>
/// Adding a player who is already shortlisted merely updates the note, so a retried add is a no-op rather than
/// a duplicate — the same reading a retried renewal takes. The live-page uniqueness itself is a partial unique
/// index on <c>(manager_id, player_id)</c>.
/// </para>
/// </remarks>
public sealed class Shortlists
{
    private readonly ResolveOwnedClub _access;
    private readonly IManagerRepository _managers;
    private readonly IRosterQueries _roster;
    private readonly IShortlistRepository _repository;
    private readonly IMarketQueries _queries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public Shortlists(
        ResolveOwnedClub access,
        IManagerRepository managers,
        IRosterQueries roster,
        IShortlistRepository repository,
        IMarketQueries queries,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _access = access;
        _managers = managers;
        _roster = roster;
        _repository = repository;
        _queries = queries;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Reads the caller's shortlist.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ShortlistResult> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var managerId = await ResolveManagerAsync(userId, cancellationToken);

        return managerId is null
            ? new ShortlistResult(MarketOutcome.NoManagerProfile, null)
            : new ShortlistResult(
                MarketOutcome.Found,
                await _queries.GetShortlistAsync(managerId.Value, cancellationToken));
    }

    /// <summary>Adds a player to the caller's shortlist, or updates the note already kept (`SCT-3`).</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player to watch.</param>
    /// <param name="notes">The private note, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ShortlistResult> AddAsync(
        Guid userId,
        Guid playerId,
        string? notes,
        CancellationToken cancellationToken)
    {
        var managerId = await ResolveManagerAsync(userId, cancellationToken);

        if (managerId is null)
        {
            return new ShortlistResult(MarketOutcome.NoManagerProfile, null);
        }

        if (await _roster.GetListingEligibilityAsync(playerId, cancellationToken) is null)
        {
            return new ShortlistResult(MarketOutcome.NotFound, null);
        }

        var now = _clock.UtcNow;
        var existing = await _repository.FindAsync(managerId.Value, playerId, cancellationToken);

        if (existing is null)
        {
            _repository.Add(ShortlistEntry.Add(
                Guid.CreateVersion7(),
                managerId.Value,
                playerId,
                notes,
                now));
        }
        else
        {
            existing.UpdateNotes(notes, now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ShortlistResult(
            MarketOutcome.Found,
            await _queries.GetShortlistAsync(managerId.Value, cancellationToken));
    }

    /// <summary>Removes a player from the caller's shortlist.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="playerId">The player to stop watching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ShortlistResult> RemoveAsync(
        Guid userId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var managerId = await ResolveManagerAsync(userId, cancellationToken);

        if (managerId is null)
        {
            return new ShortlistResult(MarketOutcome.NoManagerProfile, null);
        }

        var existing = await _repository.FindAsync(managerId.Value, playerId, cancellationToken);

        if (existing is null)
        {
            return new ShortlistResult(MarketOutcome.NotFound, null);
        }

        _repository.Remove(existing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ShortlistResult(
            MarketOutcome.Found,
            await _queries.GetShortlistAsync(managerId.Value, cancellationToken));
    }

    private async Task<Guid?> ResolveManagerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        return manager?.Id;
    }
}

using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>The result of reading a club's contract list.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Contracts">The contract list, when the read succeeded.</param>
public sealed record ListContractsResult(SquadReadOutcome Outcome, ContractsResponse? Contracts);

/// <summary>
/// Reads the contract list of the club the caller manages (master plan §10.3; `CON-1`, `CON-8`).
/// </summary>
/// <remarks>
/// The endpoint takes no club, so the subject is resolved from the tenure rather than compared against a
/// request. Wages are club state (`data-classification.md` §2), which is why this is the manager's own
/// read and why the response carries a payroll total the finance work in Stage 9 will build on.
/// </remarks>
public sealed class ListContracts
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public ListContracts(ResolveOwnedClub access, ISquadQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the contract list, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ListContractsResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new ListContractsResult(access.Outcome.ToReadOutcome(), null);
        }

        var contracts = await _queries.GetContractsAsync(access.ClubId, cancellationToken);

        return contracts is null
            ? new ListContractsResult(SquadReadOutcome.ClubNotFound, null)
            : new ListContractsResult(SquadReadOutcome.Found, contracts.ToResponse(_clock.UtcNow));
    }
}

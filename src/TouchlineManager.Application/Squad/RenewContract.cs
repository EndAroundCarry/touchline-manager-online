using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>
/// Accepts a renewal: the club re-signs the player on the quoted terms (`CON-3`, `CON-4`).
/// </summary>
/// <remarks>
/// <para>
/// The renewal re-signs the player now — the current contract closes as renewed and a new one begins this
/// season — because a player holds exactly one active contract at a time (`SQ-6`). That is the same
/// close-then-create shape a transfer uses (`CON-5`), so the new wage applies at once and there is never a
/// window in which the player holds two deals.
/// </para>
/// <para>
/// The command is conditional: the contract's version must match the one the manager read, so a renewal
/// agreed on one device cannot overwrite a change made on another. A concurrency failure between the read
/// and the write is reported as a stale precondition rather than retried silently (`CONC-1`, ADR-0009).
/// </para>
/// </remarks>
public sealed class RenewContract
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly ISquadRepository _repository;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public RenewContract(
        ResolveOwnedClub access,
        ISquadQueries queries,
        ISquadRepository repository,
        IClock clock,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _queries = queries;
        _repository = repository;
        _clock = clock;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Renews a player's contract, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="contractId">The contract to renew.</param>
    /// <param name="seasons">The length to sign, 1–3 seasons.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RenewContractResult> ExecuteAsync(
        Guid userId,
        Guid contractId,
        int seasons,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!ContractRenewal.IsLegalTerm(seasons))
        {
            return new RenewContractResult(ContractRenewalOutcome.InvalidTerm, null);
        }

        if (expectedVersion is null)
        {
            return new RenewContractResult(ContractRenewalOutcome.PreconditionRequired, null);
        }

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new RenewContractResult(ContractRenewal.FromAccess(access.Outcome), null);
        }

        var context = await _queries.GetRenewalContextAsync(contractId, cancellationToken);

        if (context is null || context.ClubId != access.ClubId)
        {
            return new RenewContractResult(ContractRenewalOutcome.ContractNotFound, null);
        }

        if (context.ContractVersion != expectedVersion.Value)
        {
            return new RenewContractResult(ContractRenewalOutcome.PreconditionFailed, null);
        }

        var contract = await _repository.FindContractAsync(contractId, cancellationToken);

        // A contract that vanished between the read and the load is a stale view, not a legal write.
        if (contract is null || contract.ClubId != access.ClubId || !contract.IsActive)
        {
            return new RenewContractResult(ContractRenewalOutcome.ContractNotFound, null);
        }

        var now = _clock.UtcNow;
        var start = context.CurrentSeasonNumber;
        var end = start + seasons - 1;
        var terms = ContractRenewalQuote.Calculate(ContractRenewal.ToInput(context), seasons);

        contract.Close(PlayerContractCloseReasons.Renewed, now);

        var renewed = PlayerContract.Sign(
            Guid.CreateVersion7(),
            context.PlayerId,
            context.ClubId,
            start,
            end,
            terms.WeeklyWageMinor,
            context.SquadStatus,
            now);

        _repository.AddPlayerContract(renewed);

        _audit.Record(new AuditEntry(
            SquadAuditActions.ContractRenewed,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.PlayerContract,
            renewed.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The contract moved between the read and the write. Report a stale precondition and let the
            // manager reload, rather than signing a second deal over the one that won the race.
            return new RenewContractResult(ContractRenewalOutcome.PreconditionFailed, null);
        }

        return new RenewContractResult(
            ContractRenewalOutcome.Renewed,
            new ContractRenewalResponse(
                renewed.Id,
                context.PlayerId,
                seasons,
                start,
                end,
                terms.WeeklyWageMinor,
                renewed.Version,
                now));
    }
}

using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Auth;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Squad;

/// <summary>
/// Releases a player now: the club pays half the wages left on the contract and the player joins the free-agent
/// pool (`CON-12`, `FA-3`).
/// </summary>
/// <remarks>
/// <para>
/// One transaction closes the contract as released, ends the registration, makes the player a free agent, posts
/// the payout and tells the manager. The weekly wage stops by itself, because wages are summed from active
/// contracts. A team sheet that already named the player is repaired when it locks, exactly as it is after a
/// transfer (`SQ-7`), so nothing here touches one.
/// </para>
/// <para>
/// The command is conditional: the contract's version must match the one the manager was quoted, so a release
/// agreed on a stale read is refused (`CONC-1`). That also makes a retry harmless — the first call moved the
/// version on and closed the contract, so the second can neither pay twice nor release twice — and the payout's
/// correlation key names the contract, which is the same guarantee at the money's own layer (`FIN-17`).
/// </para>
/// </remarks>
public sealed class ReleasePlayer
{
    private readonly ResolveOwnedClub _access;
    private readonly ISquadQueries _queries;
    private readonly ISquadRepository _squad;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IInboxRepository _inbox;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public ReleasePlayer(
        ResolveOwnedClub access,
        ISquadQueries queries,
        ISquadRepository squad,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IInboxRepository inbox,
        IClock clock,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _queries = queries;
        _squad = squad;
        _accounts = accounts;
        _ledger = ledger;
        _inbox = inbox;
        _clock = clock;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Releases a player, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="contractId">The contract to close.</param>
    /// <param name="expectedVersion">The version from the request's <c>If-Match</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReleasePlayerResult> ExecuteAsync(
        Guid userId,
        Guid contractId,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        if (expectedVersion is null)
        {
            return Refused(ContractReleaseOutcome.PreconditionRequired);
        }

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return Refused(ContractRelease.FromAccess(access.Outcome));
        }

        var context = await _queries.GetReleaseContextAsync(contractId, cancellationToken);

        if (context is null || context.ClubId != access.ClubId)
        {
            return Refused(ContractReleaseOutcome.ContractNotFound);
        }

        if (context.ContractVersion != expectedVersion.Value)
        {
            return Refused(ContractReleaseOutcome.PreconditionFailed);
        }

        var account = await _accounts.FindByClubAsync(access.ClubId, cancellationToken);
        var contract = await _squad.FindContractAsync(contractId, cancellationToken);
        var registration = await _squad.FindActiveRegistrationAsync(context.PlayerId, cancellationToken);
        var player = (await _squad.LoadPlayersAsync([context.PlayerId], cancellationToken)).SingleOrDefault();

        // A contract that vanished between the read and the load is a stale view, not a legal write.
        if (contract is null || !contract.IsActive || contract.ClubId != access.ClubId)
        {
            return Refused(ContractReleaseOutcome.ContractNotFound);
        }

        if (account is null || player is null)
        {
            return Refused(ContractReleaseOutcome.ClubNotFound);
        }

        var now = _clock.UtcNow;
        var quote = ContractRelease.Price(context, now);
        var refusal = ContractRelease.Refusal(context, quote, account.AvailableMinor);

        if (refusal is { } blocked)
        {
            return Refused(blocked);
        }

        contract.Close(PlayerContractCloseReasons.Released, now);
        registration?.End(now);
        player.ReleaseToFreeAgency(now);

        if (quote.PayoutMinor > 0)
        {
            _ledger.Add(account.Post(
                LedgerPostings.ReleasePayout(
                    Guid.CreateVersion7(),
                    access.ClubId,
                    contractId,
                    quote.RemainingWeeks,
                    quote.PayoutMinor),
                now));
        }

        await NotifyAsync(access.ClubId, context.PlayerId, quote.PayoutMinor, now, cancellationToken);

        _audit.Record(new AuditEntry(
            SquadAuditActions.ContractReleased,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.PlayerContract,
            contractId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: $"{quote.RemainingWeeks} weeks left, paid {quote.PayoutMinor}"));

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The contract or the account moved between the read and the write. Report a stale precondition
            // and let the manager reload, rather than releasing over the change that won the race.
            return Refused(ContractReleaseOutcome.PreconditionFailed);
        }

        return new ReleasePlayerResult(
            ContractReleaseOutcome.Released,
            new ReleasePlayerResponse(
                contractId,
                context.PlayerId,
                quote.PayoutMinor,
                quote.RemainingWeeks,
                context.WeeklyWageMinor,
                ContractRelease.JoinsPoolAt(now),
                now));
    }

    private async Task NotifyAsync(
        Guid clubId,
        Guid playerId,
        long payoutMinor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = (await _inbox.FindClubTargetsAsync([clubId], cancellationToken))
            .FirstOrDefault(candidate => candidate.ClubId == clubId);

        if (target?.ManagerId is not { } managerId || managerId == Guid.Empty)
        {
            return;
        }

        var names = await _inbox.FindPlayerNamesAsync([playerId], cancellationToken);
        var playerName = names.TryGetValue(playerId, out var name) ? name : "a player";
        var draft = InboxTemplates.Released(playerName, payoutMinor, playerId);

        _inbox.Add(InboxMessage.Record(
            Guid.CreateVersion7(),
            managerId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            draft.RelatedEntityId,
            now));
    }

    private static ReleasePlayerResult Refused(ContractReleaseOutcome outcome) => new(outcome, null);
}

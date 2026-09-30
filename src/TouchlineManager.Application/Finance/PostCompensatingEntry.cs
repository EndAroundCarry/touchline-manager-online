using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Ops;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Finance;

/// <summary>What happened when an operator posted a compensating finance entry.</summary>
public enum PostCompensatingEntryOutcome
{
    /// <summary>The correcting entry was posted.</summary>
    Applied = 0,

    /// <summary>No club account exists for the requested club.</summary>
    ClubNotFound = 1,

    /// <summary>The entry to correct does not exist, or belongs to another club.</summary>
    EntryNotFound = 2,

    /// <summary>This idempotency key has already posted a compensating entry.</summary>
    AlreadyPosted = 3,

    /// <summary>The correction would take cash below zero or below the club's reserved funds.</summary>
    NotAffordable = 4,

    /// <summary>The correction moves nothing.</summary>
    InvalidAmount = 5,
}

/// <summary>The result of a compensating finance entry.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="ClubId">The club that was addressed.</param>
/// <param name="EntryId">The compensating entry, when one was posted.</param>
/// <param name="ResultingCashMinor">The club's cash after the move, when one was applied.</param>
/// <param name="ReversesEntryId">The entry that was corrected, when the operator named one.</param>
public sealed record PostCompensatingEntryResult(
    PostCompensatingEntryOutcome Outcome,
    Guid ClubId,
    Guid? EntryId = null,
    long? ResultingCashMinor = null,
    Guid? ReversesEntryId = null);

/// <summary>
/// Posts an audited compensating ledger entry on an operator's authority (master plan §10.8, §13, `FIN-12`,
/// `F-46`).
/// </summary>
/// <remarks>
/// <para>
/// A balance is never edited (`FIN-12`): the repair is a new <see cref="LedgerCategory.Compensation"/> entry
/// that moves the club's cash and names the line it corrects. The corrected entry is left exactly as it was,
/// so the ledger still replays to the balances the club actually held (`FIN-18`).
/// </para>
/// <para>
/// The operator's idempotency key becomes the entry's correlation key, so the ledger's own
/// <c>unique (correlation_id, category)</c> index is the guarantee: a replayed repair collides with its first
/// entry rather than posting a second one (`FIN-17`), and the sequential-retry read turns that into a clean
/// conflict for the operator.
/// </para>
/// </remarks>
public sealed class PostCompensatingEntry
{
    private readonly IClock _clock;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public PostCompensatingEntry(
        IClock clock,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _accounts = accounts;
        _ledger = ledger;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Posts the correcting entry.</summary>
    /// <param name="clubId">The club whose ledger is corrected.</param>
    /// <param name="cashDeltaMinor">The signed correction to cash.</param>
    /// <param name="reversesEntryId">The entry being corrected, or null when the operator names none.</param>
    /// <param name="reason">Why the operator is doing it. Required, and stored in the audit trail.</param>
    /// <param name="idempotencyKey">The operator's idempotency key, which becomes the entry's correlation key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PostCompensatingEntryResult> ExecuteAsync(
        Guid clubId,
        long cashDeltaMinor,
        Guid? reversesEntryId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        AccountAdministration.ValidateReason(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (cashDeltaMinor == 0)
        {
            return new PostCompensatingEntryResult(PostCompensatingEntryOutcome.InvalidAmount, clubId);
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        var account = await _accounts.FindByClubAsync(clubId, cancellationToken);

        if (account is null)
        {
            return new PostCompensatingEntryResult(PostCompensatingEntryOutcome.ClubNotFound, clubId);
        }

        if (reversesEntryId is { } originalId)
        {
            var original = await _ledger.FindByIdAsync(originalId, cancellationToken);

            if (original is null || original.ClubId != clubId)
            {
                return new PostCompensatingEntryResult(PostCompensatingEntryOutcome.EntryNotFound, clubId);
            }
        }

        var existing = await _ledger.FindExistingCorrelationIdsAsync([idempotencyKey], cancellationToken);

        if (existing.Contains(idempotencyKey))
        {
            return new PostCompensatingEntryResult(PostCompensatingEntryOutcome.AlreadyPosted, clubId);
        }

        // FIN-10 and FIN-13, expressed before the account's own guard so the operator gets a conflict rather
        // than an exception: a debit may not take cash below zero, nor below the funds already reserved.
        if (account.CashMinor + cashDeltaMinor < account.ReservedMinor)
        {
            return new PostCompensatingEntryResult(PostCompensatingEntryOutcome.NotAffordable, clubId);
        }

        var entry = account.Post(
            LedgerPostings.Compensation(
                Guid.CreateVersion7(),
                clubId,
                cashDeltaMinor,
                idempotencyKey,
                reversesEntryId),
            _clock.UtcNow);

        _ledger.Add(entry);

        _audit.Record(new AuditEntry(
            FinanceAuditActions.CompensatingEntry,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.LedgerEntry,
            entry.Id,
            idempotencyKey,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PostCompensatingEntryResult(
            PostCompensatingEntryOutcome.Applied,
            clubId,
            entry.Id,
            entry.ResultingCashMinor,
            reversesEntryId);
    }
}

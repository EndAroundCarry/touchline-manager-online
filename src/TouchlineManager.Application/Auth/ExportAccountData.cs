using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Assembles the account's own data as one machine-readable document (master plan §12.4, F-07).
/// </summary>
/// <remarks>
/// <para>
/// The export is scoped from the authenticated account, so it can only ever be of the caller's own data,
/// and it reads no secret or hidden value: no password hash, no token hash, no client-fingerprint hash, and
/// nothing from a server-only column (`VOI-4`, data classification §4).
/// </para>
/// <para>
/// "Own transactional history" is the ledger of the club the account currently manages, which the manager
/// already reads through the finances screen. Past clubs are covered by the tenure history rather than by
/// their ledgers: a club's money and results outlive the managers who ran it and are world data, not the
/// departing manager's personal record.
/// </para>
/// </remarks>
public sealed class ExportAccountData
{
    /// <summary>How many ledger entries the export includes before it reports itself truncated.</summary>
    private const int MaxLedgerEntries = 1000;

    /// <summary>How many ledger entries each read returns, so the walk is bounded per round trip.</summary>
    private const int LedgerPageSize = 200;

    private readonly IClock _clock;
    private readonly IUserRepository _users;
    private readonly IManagerRepository _managers;
    private readonly IClubTenureRepository _tenures;
    private readonly IClubRepository _clubs;
    private readonly IRefreshSessionRepository _sessions;
    private readonly IFinanceQueries _finance;
    private readonly ISecureTokenService _secureTokens;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public ExportAccountData(
        IClock clock,
        IUserRepository users,
        IManagerRepository managers,
        IClubTenureRepository tenures,
        IClubRepository clubs,
        IRefreshSessionRepository sessions,
        IFinanceQueries finance,
        ISecureTokenService secureTokens,
        IAuditWriter audit,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _users = users;
        _managers = managers;
        _tenures = tenures;
        _clubs = clubs;
        _sessions = sessions;
        _finance = finance;
        _secureTokens = secureTokens;
        _audit = audit;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Builds the export, or <see langword="null"/> when the account no longer exists.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AccountExportResponse?> ExecuteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var consents = await _users.ListConsentsAsync(userId, cancellationToken);
        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);
        var tenures = manager is null
            ? []
            : await _tenures.ListByManagerAsync(manager.Id, cancellationToken);

        var clubNames = await LoadClubNamesAsync(tenures, cancellationToken);
        var sessions = await _sessions.FindActiveByUserIdAsync(userId, cancellationToken);
        var finance = await LoadFinanceAsync(tenures, cancellationToken);

        _audit.Record(new AuditEntry(
            AuthAuditActions.AccountExported,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.User,
            userId,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: null));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountExportResponse(
            now,
            new ExportedAccountResponse(
                user.Id,
                user.Email,
                user.DisplayName,
                user.EmailVerifiedAt.HasValue,
                user.Status.ToCode(),
                user.RoleNames(),
                user.LastLoginAt,
                user.CreatedAt),
            [.. consents.Select(consent => new ExportedConsentResponse(
                consent.DocumentType,
                consent.Version,
                consent.AcceptedAt))],
            manager is null
                ? null
                : new ExportedManagerResponse(
                    manager.Id,
                    manager.Reputation,
                    manager.Locale,
                    manager.TimeZone,
                    manager.TakeoverCooldownUntil,
                    manager.CreatedAt),
            [.. tenures.Select(tenure => new ExportedTenureResponse(
                tenure.Id,
                tenure.ClubId,
                clubNames.TryGetValue(tenure.ClubId, out var name) ? name : string.Empty,
                tenure.ControlStatus.ToCode(),
                tenure.StartedAt,
                tenure.EndedAt,
                tenure.EndReason))],
            [.. sessions
                .Where(session => session.IsActive(now))
                .Select(session => new ExportedSessionResponse(
                    session.Id,
                    session.IssuedAt,
                    session.ExpiresAt,
                    session.LastUsedAt))],
            finance);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadClubNamesAsync(
        IReadOnlyList<ClubTenure> tenures,
        CancellationToken cancellationToken)
    {
        var clubIds = tenures.Select(tenure => tenure.ClubId).Distinct().ToList();

        if (clubIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var clubs = await _clubs.LoadAsync(clubIds, cancellationToken);

        return clubs.ToDictionary(club => club.Id, club => club.Name);
    }

    private async Task<ExportedFinanceResponse?> LoadFinanceAsync(
        IReadOnlyList<ClubTenure> tenures,
        CancellationToken cancellationToken)
    {
        var open = tenures.FirstOrDefault(tenure => tenure.IsOpen);

        if (open is null)
        {
            return null;
        }

        var entries = new List<LedgerEntry>();
        long? beforeSequence = null;
        var truncated = false;

        while (true)
        {
            var page = await _finance.GetFinanceLedgerAsync(
                open.ClubId,
                beforeSequence,
                LedgerPageSize,
                cancellationToken);

            if (page.Entries.Count == 0)
            {
                break;
            }

            foreach (var entry in page.Entries)
            {
                if (entries.Count >= MaxLedgerEntries)
                {
                    truncated = true;

                    break;
                }

                entries.Add(entry);
            }

            if (truncated || !page.HasMore)
            {
                break;
            }

            beforeSequence = page.Entries[^1].Sequence;
        }

        return new ExportedFinanceResponse(
            open.ClubId,
            [.. entries.Select(entry => new ExportedLedgerEntryResponse(
                entry.Sequence,
                entry.Category.ToCode(),
                entry.CashDeltaMinor,
                entry.ReservedDeltaMinor,
                entry.ResultingCashMinor,
                entry.ResultingReservedMinor,
                entry.DescriptionTemplate,
                entry.CreatedAt))],
            truncated);
    }
}

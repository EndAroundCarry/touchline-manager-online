using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Finance;

/// <summary>What a week's finance run settled.</summary>
/// <param name="Clubs">How many clubs the world holds.</param>
/// <param name="Skipped">How many clubs were already settled for the week (`FIN-17`).</param>
/// <param name="Posted">How many ledger entries were written.</param>
/// <param name="Grants">How many clubs needed an emergency grant (`FIN-16`).</param>
/// <param name="GrantedMinor">The total granted, in minor units.</param>
public sealed record RunWeeklyFinanceResult(
    int Clubs,
    int Skipped,
    int Posted,
    int Grants,
    long GrantedMinor);

/// <summary>
/// Settles one week's sponsorship, wages, and operating cost for every club, granting emergency funds where
/// a club cannot pay (`CON-2`, `FIN-4`, `FIN-7`, `FIN-9`, `FIN-16`).
/// </summary>
/// <remarks>
/// <para>
/// The plan gives wages, sponsorship, and the operating cost to one durable run (`WeeklyFinanceRun`), and
/// this is it. It is one transaction for the whole world so a week either settles everywhere or nowhere: a
/// run that posts half the clubs and fails would leave a week that is neither owed nor paid, and the
/// queue's retry would only ever fix it if the partial write were not committed.
/// </para>
/// <para>
/// Idempotency is the ledger's, not the run's: every posting for a club shares one correlation key per week
/// and carries its own category, so a retried run collides with the entries it already wrote rather than
/// charging twice (`FIN-17`). The run is therefore safe to replay whenever the worker restarts.
/// </para>
/// <para>
/// The safety step runs before the wages, not instead of them: a club that cannot cover the week is granted
/// exactly the shortfall, recorded as an audit action and raised as an operations alert, so the game
/// preserves competition integrity without hiding that it had to (`FIN-16`).
/// </para>
/// </remarks>
public sealed partial class RunWeeklyFinance
{
    private readonly IFinanceQueries _queries;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<RunWeeklyFinance> _logger;

    /// <summary>Initializes the use case.</summary>
    public RunWeeklyFinance(
        IFinanceQueries queries,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<RunWeeklyFinance> logger)
    {
        _queries = queries;
        _accounts = accounts;
        _ledger = ledger;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Settles one week for every club.</summary>
    /// <param name="weekOf">The week's boundary date, which keys every posting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RunWeeklyFinanceResult> ExecuteAsync(
        DateOnly weekOf,
        CancellationToken cancellationToken)
    {
        var obligations = await _queries.GetWeeklyObligationsAsync(cancellationToken);

        if (obligations.Count == 0)
        {
            return new RunWeeklyFinanceResult(0, 0, 0, 0, 0);
        }

        var accounts = (await _accounts.LoadAllAsync(cancellationToken))
            .ToDictionary(account => account.ClubId);

        // A club whose week already has an entry is settled, so a retried run skips it cleanly rather than
        // colliding with the rows it already wrote (FIN-17). The unique index remains the guard against a
        // concurrent double-write; this read is what makes a sequential retry a no-op.
        var correlationByClub = obligations.ToDictionary(
            obligation => obligation.ClubId,
            obligation => LedgerPostings.WeeklyCorrelationId(weekOf, obligation.ClubId));

        var settled = await _ledger.FindExistingCorrelationIdsAsync(
            [.. correlationByClub.Values],
            cancellationToken);

        var now = _clock.UtcNow;
        var skipped = 0;
        var posted = 0;
        var grants = 0;
        long grantedMinor = 0;

        // Ordered so a replay writes the same entries in the same order, which keeps the ledger's sequence
        // numbers reproducible for the clubs a retry touches again.
        foreach (var club in obligations.OrderBy(obligation => obligation.ClubId))
        {
            if (settled.Contains(correlationByClub[club.ClubId]))
            {
                skipped++;

                continue;
            }

            if (!accounts.TryGetValue(club.ClubId, out var account))
            {
                throw new InvalidOperationException(
                    $"Club {club.ClubId:D} has no account, so its week cannot be settled (FIN-11).");
            }

            var sponsorship = WorldRuleSet.WeeklySponsorshipMinorForTier(club.TierNumber);

            if (sponsorship > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.WeeklySponsorship(
                        Guid.CreateVersion7(),
                        club.ClubId,
                        weekOf,
                        club.TierNumber,
                        sponsorship),
                    now));

                posted++;
            }

            var operating = WorldRuleSet.WeeklyOperatingCostMinorForTier(club.TierNumber);
            var outgoings = club.WeeklyWageMinor + operating;

            // The week's credit is in the account before the guard reads it, because it is the week's income.
            var shortfall = ClubSolvencyGuard.Shortfall(account.AvailableMinor, outgoings);

            if (shortfall > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.EmergencyGrant(Guid.CreateVersion7(), club.ClubId, weekOf, shortfall),
                    now));

                _audit.Record(new AuditEntry(
                    FinanceAuditActions.EmergencyGrant,
                    AuditActorTypes.Service,
                    ActorUserId: null,
                    AuditTargetTypes.Club,
                    club.ClubId,
                    LedgerPostings.WeeklyCorrelationId(weekOf, club.ClubId),
                    IpHash: null,
                    $"Emergency grant of {shortfall} minor units to cover the week of "
                    + $"{weekOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)} (FIN-16)."));

                // An operations alert, not a quiet correction: a grant means the economy is not yet balanced
                // for this club, which is exactly what an operator needs to see (FIN-16).
                LogGranted(club.ClubId, weekOf, shortfall);

                posted++;
                grants++;
                grantedMinor += shortfall;
            }

            if (club.WeeklyWageMinor > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.Wages(
                        Guid.CreateVersion7(),
                        club.ClubId,
                        weekOf,
                        club.ContractedPlayers,
                        club.WeeklyWageMinor),
                    now));

                posted++;
            }

            if (operating > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.OperatingCost(Guid.CreateVersion7(), club.ClubId, weekOf, operating),
                    now));

                posted++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogSettled(weekOf, obligations.Count, skipped, posted, grants, grantedMinor);

        return new RunWeeklyFinanceResult(obligations.Count, skipped, posted, grants, grantedMinor);
    }

    [LoggerMessage(
        EventId = 3300,
        Level = LogLevel.Information,
        Message = "Settled the week of {WeekOf}: {ClubCount} clubs, {SkippedCount} already settled, {EntryCount} entries, {GrantCount} grants totalling {GrantedMinor} minor units.")]
    private partial void LogSettled(
        DateOnly weekOf,
        int clubCount,
        int skippedCount,
        int entryCount,
        int grantCount,
        long grantedMinor);

    [LoggerMessage(
        EventId = 3301,
        Level = LogLevel.Warning,
        Message = "OPERATIONS ALERT: club {ClubId} could not pay the week of {WeekOf} and was granted {ShortfallMinor} minor units to preserve competition integrity (FIN-16).")]
    private partial void LogGranted(Guid clubId, DateOnly weekOf, long shortfallMinor);
}

using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Finance;

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
/// Settles one week's merchandise, operating cost, ground upkeep and structure upkeep for every club, granting emergency funds where a club
/// would pass its overdraft floor (`FIN-9`, `STRC-6`, `STRC-8`, `STAD-7`, `FIN-16`).
/// </summary>
/// <remarks>
/// <para>
/// The wages, the league's television money and the sponsor's payment are not here: they are paid on every matchday, when the round is
/// published (`FIN-4`, `FIN-7`). This run keeps what is naturally weekly: the shop sells to the fans as they close it, and the club's fixed
/// costs fall due. It is one transaction for the whole world so a week either settles everywhere or nowhere: a run that posts half the clubs
/// and fails would leave a week that is neither owed nor paid, and the queue's retry would only ever fix it if the partial write were not
/// committed.
/// </para>
/// <para>
/// Idempotency is the ledger's, not the run's: every posting for a club shares one correlation key per week and carries its own category, so
/// a retried run collides with the entries it already wrote rather than charging twice (`FIN-17`). The run is therefore safe to replay
/// whenever the worker restarts.
/// </para>
/// <para>
/// The costs may take a club's cash below zero, down to its overdraft floor, so they are never refused; a cost that would pass the floor is
/// met by a grant of exactly the difference, recorded as an audit action and raised as an operations alert, so the game preserves competition
/// integrity without hiding that it had to (`FIN-16`). After the week the clubs' money is read, and a club that crossed into the red or into
/// administration is moved and told (`DEBT-2`).
/// </para>
/// </remarks>
public sealed partial class RunWeeklyFinance
{
    private readonly IFinanceQueries _queries;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IStructureRepository _structures;
    private readonly RecurringCostRoom _room;
    private readonly ClubFinancialStates _states;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<RunWeeklyFinance> _logger;

    /// <summary>Initializes the use case.</summary>
    public RunWeeklyFinance(
        IFinanceQueries queries,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IStructureRepository structures,
        RecurringCostRoom room,
        ClubFinancialStates states,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<RunWeeklyFinance> logger)
    {
        _queries = queries;
        _accounts = accounts;
        _ledger = ledger;
        _structures = structures;
        _room = room;
        _states = states;
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
        var settledClubs = new List<Guid>();

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

            // The shop sells all week to the fans as they close it, and its credit is in the account before the
            // costs are met, like any income (STRC-6, STRC-7).
            var moodBonus = StructureRuleSet.MoodBonusFor(FanMoods.WordFor(club.MoodBp));
            var merchBase = StructureRuleSet.MerchBaseMinorFor(club.MerchandiseLevel, club.Fans, club.TierNumber);
            var merchandise = StructureRuleSet.WithBonus(merchBase, moodBonus.Bp);

            if (merchandise > 0)
            {
                var entryId = Guid.CreateVersion7();

                _ledger.Add(account.Post(
                    LedgerPostings.MerchandiseTakings(
                        entryId,
                        club.ClubId,
                        weekOf,
                        club.MerchandiseLevel,
                        club.Fans,
                        moodBonus.Bp,
                        merchandise),
                    now));

                _structures.AddTakings(ClubStructureTakings.WeekSale(
                    Guid.CreateVersion7(),
                    club.ClubId,
                    weekOf,
                    club.MerchandiseLevel,
                    club.Fans,
                    merchBase,
                    moodBonus.Bp,
                    moodBonus.Reason,
                    merchandise,
                    entryId,
                    now));

                posted++;
            }

            var operating = EconomyRuleSet.PerWeeklyRun(EconomyRuleSet.OperatingCostSeasonMinorFor(club.TierNumber));
            var groundUpkeep = EconomyRuleSet.PerWeeklyRun(StadiumRuleSet.GroundUpkeepSeasonMinorFor(club.Seats, club.TierNumber));
            var snackUpkeep = StructureRuleSet.UpkeepMinorFor(club.SnackBarLevel, club.TierNumber);
            var merchUpkeep = StructureRuleSet.UpkeepMinorFor(club.MerchandiseLevel, club.TierNumber);
            var upkeep = snackUpkeep + merchUpkeep;

            // The costs are owed whatever the shop earned, so they are made room for together (STRC-8, STAD-7).
            var room = await _room.MakeRoomAsync(
                account,
                operating + groundUpkeep + upkeep,
                amount => LedgerPostings.EmergencyGrant(Guid.CreateVersion7(), club.ClubId, weekOf, amount),
                correlationByClub[club.ClubId],
                now,
                cancellationToken);

            if (room.GrantedMinor > 0)
            {
                posted++;
                grants++;
                grantedMinor += room.GrantedMinor;
            }

            posted += room.WithdrawnBids;

            if (operating > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.OperatingCost(Guid.CreateVersion7(), club.ClubId, weekOf, operating),
                    now));

                posted++;
            }

            if (groundUpkeep > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.GroundUpkeep(Guid.CreateVersion7(), club.ClubId, weekOf, club.Seats.Total, groundUpkeep),
                    now));

                posted++;
            }

            // One entry for both buildings, because the ledger allows one entry per category per week; the
            // per-building split is in the takings rows (STRC-8).
            var upkeepEntryId = Guid.CreateVersion7();

            _ledger.Add(account.Post(
                LedgerPostings.StructureUpkeep(
                    upkeepEntryId,
                    club.ClubId,
                    weekOf,
                    club.SnackBarLevel,
                    club.MerchandiseLevel,
                    upkeep),
                now));

            _structures.AddTakings(ClubStructureTakings.Upkeep(
                Guid.CreateVersion7(), club.ClubId, StructureKind.SnackBar, weekOf, club.SnackBarLevel, snackUpkeep, upkeepEntryId, now));

            _structures.AddTakings(ClubStructureTakings.Upkeep(
                Guid.CreateVersion7(), club.ClubId, StructureKind.Merchandise, weekOf, club.MerchandiseLevel, merchUpkeep, upkeepEntryId, now));

            posted++;
            settledClubs.Add(club.ClubId);
        }

        // The week's costs may have taken a club into the red; the matchday ordinal is the last round any division has published.
        await _states.ApplyAsync(
            settledClubs,
            await _queries.GetSettledMatchdayOrdinalAsync(cancellationToken),
            $"weekly-run:{weekOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}",
            now,
            cancellationToken);

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
}

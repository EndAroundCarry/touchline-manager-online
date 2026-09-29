using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Finance;

/// <summary>One club's final placing, as the season's settlement reads it (`FIN-5`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="TierNumber">The tier it finished in.</param>
/// <param name="FinalRank">Its final position, 1-based.</param>
public sealed record SeasonSettlementClub(Guid ClubId, int TierNumber, int FinalRank);

/// <summary>What settling a season's finances did.</summary>
/// <param name="Awarded">How many clubs received a final-position award.</param>
/// <param name="Summaries">How many season finance summaries were written.</param>
public sealed record SeasonSettlementResult(int Awarded, int Summaries);

/// <summary>
/// Settles a closing season's money: the final-position awards (`FIN-5`) and each club's season finance
/// summary (master plan §6.8, §7.5).
/// </summary>
/// <remarks>
/// <para>
/// Runs inside the rollover's <c>Finalized</c> phase transaction, so the awards and the summaries commit
/// with the checkpoint and a crash leaves neither applied. It is idempotent by construction: the phase guard
/// stops a second run, the position award's ledger correlation key collides on a retry (`FIN-17`), and the
/// summary's <c>unique (club_id, season_id)</c> refuses a duplicate.
/// </para>
/// <para>
/// The award is posted after the closing entries have recorded their cash, so a club's recorded closing cash
/// stays the figure its season actually ended on; the award is the first money of the next season. The
/// summary's window is the season's own — from its start to the end of its rollover — so the same award is
/// deliberately outside it.
/// </para>
/// </remarks>
public sealed partial class SettleSeasonFinances
{
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IClubSeasonFinanceRepository _summaries;
    private readonly ILogger<SettleSeasonFinances> _logger;

    /// <summary>Initializes the settlement.</summary>
    public SettleSeasonFinances(
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IClubSeasonFinanceRepository summaries,
        ILogger<SettleSeasonFinances> logger)
    {
        _accounts = accounts;
        _ledger = ledger;
        _summaries = summaries;
        _logger = logger;
    }

    /// <summary>Settles the closing season's awards and writes each club's finance summary.</summary>
    /// <param name="worldId">The world.</param>
    /// <param name="season">The closing season.</param>
    /// <param name="clubs">Every club's tier and final placing.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SeasonSettlementResult> SettleAsync(
        Guid worldId,
        Season season,
        IReadOnlyList<SeasonSettlementClub> clubs,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(clubs);

        if (clubs.Count == 0)
        {
            return new SeasonSettlementResult(0, 0);
        }

        var clubIds = clubs.Select(club => club.ClubId).Distinct().ToList();
        var accounts = (await _accounts.LoadAsync(clubIds, cancellationToken))
            .ToDictionary(account => account.ClubId);

        // Awards first, so they are staged before the summary reads the ledger; they are outside the summary's
        // window either way, so the two do not depend on each other's order.
        var awarded = 0;

        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            if (!accounts.TryGetValue(club.ClubId, out var account))
            {
                continue;
            }

            var amount = WorldRuleSet.PositionAwardMinorFor(club.TierNumber, club.FinalRank);

            _ledger.Add(account.Post(
                LedgerPostings.PositionAward(
                    Guid.CreateVersion7(),
                    club.ClubId,
                    season.Id,
                    club.TierNumber,
                    club.FinalRank,
                    amount),
                now));

            awarded++;
        }

        // The season's window: from the first kickoff to the end of its rollover. Movements inside it are the
        // season's income and expenses; the award just posted is after it and stays in the ledger only.
        var windowStart = season.StartsAt;
        var windowEnd = season.RolloverEndsAt > now ? season.RolloverEndsAt : now;

        var openings = await _ledger.LoadCashBalanceBeforeAsync(clubIds, windowStart, cancellationToken);
        var totals = await _ledger.LoadCategoryTotalsAsync(clubIds, windowStart, windowEnd, cancellationToken);

        var totalsByClub = totals
            .GroupBy(total => total.ClubId)
            .ToDictionary(group => group.Key, group => group.OrderBy(total => total.Category).ToList());

        var summaries = 0;

        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            var openingCash = openings.TryGetValue(club.ClubId, out var opening) ? opening : 0;
            var lines = totalsByClub.TryGetValue(club.ClubId, out var clubTotals) ? clubTotals : [];

            var closingCash = openingCash + lines.Sum(line => line.CashDeltaMinor);

            var summary = ClubSeasonFinance.Record(
                Guid.CreateVersion7(),
                club.ClubId,
                season.Id,
                openingCash,
                closingCash,
                now);

            _summaries.Add(summary);

            foreach (var line in lines)
            {
                _summaries.AddLine(ClubSeasonFinanceLine.Record(
                    Guid.CreateVersion7(),
                    summary.Id,
                    line.Category,
                    line.CashDeltaMinor,
                    now));
            }

            summaries++;
        }

        LogSettled(worldId, season.Id, awarded, summaries);

        return new SeasonSettlementResult(awarded, summaries);
    }

    [LoggerMessage(
        EventId = 5310,
        Level = LogLevel.Information,
        Message = "Settled season {SeasonId} of world {WorldId}: {Awarded} awards, {Summaries} season summaries (FIN-5).")]
    private partial void LogSettled(Guid worldId, Guid seasonId, int awarded, int summaries);
}

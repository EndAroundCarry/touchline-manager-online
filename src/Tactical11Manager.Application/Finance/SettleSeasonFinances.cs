using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Finance;

/// <summary>One club's final placing, as the season's settlement reads it (`FIN-5`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="TierNumber">The tier it finished in.</param>
/// <param name="FinalRank">Its final position, 1-based.</param>
/// <param name="NextTierNumber">The tier it plays in next season: below <paramref name="TierNumber"/> numerically for a promoted club, above it for a relegated one.</param>
public sealed record SeasonSettlementClub(Guid ClubId, int TierNumber, int FinalRank, int NextTierNumber);

/// <summary>What settling a season's finances did.</summary>
/// <param name="Awarded">How many clubs received a final-position prize.</param>
/// <param name="Summaries">How many season finance summaries were written.</param>
/// <param name="Bonuses">How many clubs received a sponsor bonus.</param>
/// <param name="Parachutes">How many relegated clubs received a parachute.</param>
/// <param name="Steps">How many clubs had their standing moved.</param>
public sealed record SeasonSettlementResult(int Awarded, int Summaries, int Bonuses, int Parachutes, int Steps);

/// <summary>
/// Settles a closing season's money: the league's prize money for the final place (`FIN-5`), the sponsor's bonus for the objective (`OBJ-4`),
/// the parachute of a relegated club, the club's standing for the next season (`STD-2`), and each club's season finance summary (master plan
/// §6.8, §7.5).
/// </summary>
/// <remarks>
/// <para>
/// Runs inside the rollover's <c>Finalized</c> phase transaction, so the money, the standing and the summaries commit with the checkpoint and a
/// crash leaves none of them applied. It is idempotent by construction: the phase guard stops a second run, the ledger's correlation key
/// collides on a retry (`FIN-17`), a deal is settled once, and the summary's <c>unique (club_id, season_id)</c> refuses a duplicate.
/// </para>
/// <para>
/// The prize is paid at the tier the club <em>finished</em> in, so a relegated club gets the prize of its place and then its parachute, and a
/// promoted club gets the lower tier's prize and then the bigger television money of the tier above. The money is posted after the closing
/// entries have recorded their cash, so a club's recorded closing cash stays the figure its season actually ended on; it is the first money of
/// the next season. The summary's window is the season's own — from its start to the end of its rollover — so the same money is deliberately
/// outside it.
/// </para>
/// </remarks>
public sealed partial class SettleSeasonFinances
{
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IClubSeasonFinanceRepository _summaries;
    private readonly ISponsorRepository _sponsors;
    private readonly IClubRepository _clubs;
    private readonly ILogger<SettleSeasonFinances> _logger;

    /// <summary>Initializes the settlement.</summary>
    public SettleSeasonFinances(
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IClubSeasonFinanceRepository summaries,
        ISponsorRepository sponsors,
        IClubRepository clubs,
        ILogger<SettleSeasonFinances> logger)
    {
        _accounts = accounts;
        _ledger = ledger;
        _summaries = summaries;
        _sponsors = sponsors;
        _clubs = clubs;
        _logger = logger;
    }

    /// <summary>Settles the closing season's money and standing and writes each club's finance summary.</summary>
    /// <param name="worldId">The world.</param>
    /// <param name="season">The closing season.</param>
    /// <param name="clubs">Every club's tier, final placing and next tier.</param>
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
            return new SeasonSettlementResult(0, 0, 0, 0, 0);
        }

        var clubIds = clubs.Select(club => club.ClubId).Distinct().ToList();
        var accounts = (await _accounts.LoadAsync(clubIds, cancellationToken))
            .ToDictionary(account => account.ClubId);
        var deals = (await _sponsors.LoadDealsAsync(season.Id, clubIds, cancellationToken))
            .ToDictionary(deal => deal.ClubId);
        var standings = (await _clubs.LoadAsync(clubIds, cancellationToken))
            .ToDictionary(club => club.Id);

        // The money and the standing first, so they are staged before the summary reads the ledger; they are outside the summary's window
        // either way, so the two do not depend on each other's order.
        var awarded = 0;
        var bonuses = 0;
        var parachutes = 0;
        var steps = 0;

        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            if (!accounts.TryGetValue(club.ClubId, out var account))
            {
                continue;
            }

            var prize = EconomyRuleSet.PrizeMinorFor(club.TierNumber, club.FinalRank);

            _ledger.Add(account.Post(
                LedgerPostings.PositionAward(
                    Guid.CreateVersion7(),
                    club.ClubId,
                    season.Id,
                    club.TierNumber,
                    club.FinalRank,
                    prize),
                now));

            awarded++;

            ObjectiveOutcome? outcome = null;

            if (deals.TryGetValue(club.ClubId, out var deal) && !deal.IsSettled)
            {
                var bonus = deal.Settle(club.FinalRank, now);

                outcome = deal.Outcome;

                if (bonus > 0)
                {
                    _ledger.Add(account.Post(
                        LedgerPostings.SponsorBonus(
                            Guid.CreateVersion7(),
                            club.ClubId,
                            season.Id,
                            deal.TargetPlace,
                            club.FinalRank,
                            bonus),
                        now));

                    bonuses++;
                }
            }

            if (club.NextTierNumber > club.TierNumber)
            {
                var parachute = EconomyRuleSet.ParachuteMinorFor(club.TierNumber);

                if (parachute > 0)
                {
                    _ledger.Add(account.Post(
                        LedgerPostings.Parachute(Guid.CreateVersion7(), club.ClubId, season.Id, club.TierNumber, parachute),
                        now));

                    parachutes++;
                }
            }

            if (standings.TryGetValue(club.ClubId, out var row))
            {
                var before = row.StandingCentis;
                var after = ClubStanding.Step(before, club.TierNumber, club.FinalRank, outcome, club.NextTierNumber);

                row.RecordStanding(after, now);

                _sponsors.AddStandingEvent(ClubStandingEvent.Record(
                    Guid.CreateVersion7(),
                    club.ClubId,
                    season.Id,
                    club.TierNumber,
                    club.NextTierNumber,
                    club.FinalRank,
                    outcome,
                    before,
                    row.StandingCentis,
                    now));

                steps++;
            }
        }

        // The season's window: from the first kickoff to the end of its rollover. Movements inside it are the
        // season's income and expenses; the money just posted is after it and stays in the ledger only.
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

        LogSettled(worldId, season.Id, awarded, bonuses, parachutes, steps, summaries);

        return new SeasonSettlementResult(awarded, summaries, bonuses, parachutes, steps);
    }

    [LoggerMessage(
        EventId = 5310,
        Level = LogLevel.Information,
        Message = "Settled season {SeasonId} of world {WorldId}: {Awarded} prizes, {Bonuses} sponsor bonuses, {Parachutes} parachutes, {Steps} standing steps, {Summaries} season summaries (FIN-5, OBJ-4, STD-2).")]
    private partial void LogSettled(Guid worldId, Guid seasonId, int awarded, int bonuses, int parachutes, int steps, int summaries);
}

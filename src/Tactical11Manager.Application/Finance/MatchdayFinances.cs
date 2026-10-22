using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Finance;

/// <summary>
/// Posts a published round's gate revenue and snack bar takings (`FIN-3`, `STRC-4`, master plan §7.4.7).
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>MatchdayNotifications</c> for money: publication calls this in the same
/// transaction that made the results public, so the gate and the result become public together, and a
/// publication that fails leaves neither behind. It stages its entries through the ledger and never saves,
/// which is what lets the caller commit them with the table it just rebuilt.
/// </para>
/// <para>
/// The gate is drawn by the fans going into the round, not the fans coming out of it, because the crowd came
/// before the result moved them: the caller applies the result to the fans only after this has run. The
/// correlation key is the fixture, so a retried publication collides with the first entry rather than
/// drawing the gate twice (`FIN-17`).
/// </para>
/// <para>
/// The snack bar sells to the same crowd the gate was drawn for, and the result lifts what it takes: a win the
/// fans did not expect lifts it most. The result is judged against the expectation frozen when the team sheets
/// locked, exactly as the fans judge it, so the fans and the snack bar agree on who was the underdog
/// (`STRC-5`). Its takings share the fixture's correlation key and are told apart from the gate by their
/// category.
/// </para>
/// </remarks>
public sealed class MatchdayFinances
{
    private readonly IFinanceQueries _queries;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IFanRepository _fans;
    private readonly IStructureRepository _structures;
    private readonly RecurringCostRoom _room;
    private readonly ClubFinancialStates _states;

    /// <summary>Initializes the collaborator.</summary>
    public MatchdayFinances(
        IFinanceQueries queries,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IFanRepository fans,
        IStructureRepository structures,
        RecurringCostRoom room,
        ClubFinancialStates states)
    {
        _queries = queries;
        _accounts = accounts;
        _ledger = ledger;
        _fans = fans;
        _structures = structures;
        _room = room;
        _states = states;
    }

    /// <summary>
    /// Posts the round's money for every club of the division: the league's television money, the sponsor's fixed part and the wages, then
    /// reads each club's money (`FIN-4`, `FIN-7`, `SPN-3`, `DEBT-2`).
    /// </summary>
    /// <param name="matchdayId">The round being published, which names the division.</param>
    /// <param name="seasonSequenceNumber">The season's ordinal in the world.</param>
    /// <param name="roundNumber">The round, 1 to the season's matchdays.</param>
    /// <param name="now">The current instant, taken from the caller's clock (`TIME-2`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What reading the clubs' money did.</returns>
    /// <remarks>
    /// <para>
    /// Every club of the division gets it, whether it played at home or away, so a season's income and wages are paid in 34 equal parts however
    /// the fixtures fall. The postings of a club share one correlation key and differ by category, so a retried publication collides with
    /// them instead of paying twice (`FIN-17`).
    /// </para>
    /// <para>
    /// The overdraft limit is set when the season's first round is published and again after the seventeenth, from the income forecast the
    /// club's ground, fans, buildings and sponsor give it then (`BUD-3`). The income is posted before the wages, so the club's cash is as high
    /// as it will be when the wages are met.
    /// </para>
    /// </remarks>
    public async Task<FinancialStatesResult> PostMatchdayMoneyAsync(
        Guid matchdayId,
        int seasonSequenceNumber,
        int roundNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var clubIds = (await _queries.GetMatchdayRevenueBasisAsync(matchdayId, cancellationToken))
            .Select(row => row.ClubId)
            .OrderBy(clubId => clubId)
            .ToList();

        if (clubIds.Count == 0)
        {
            return new FinancialStatesResult(0, 0, 0, 0);
        }

        var basis = (await _queries.GetClubFinanceBasisAsync(clubIds, cancellationToken))
            .ToDictionary(row => row.ClubId);

        var accounts = (await _accounts.LoadAsync(clubIds, cancellationToken))
            .ToDictionary(account => account.ClubId);

        foreach (var clubId in clubIds)
        {
            if (!basis.TryGetValue(clubId, out var row) || !accounts.TryGetValue(clubId, out var account))
            {
                throw new InvalidOperationException(
                    $"Club {clubId:D} was in a published division but has no basis or account to pay its matchday (FIN-11).");
            }

            if (roundNumber == 1 || account.OverdraftLimitMinor == 0)
            {
                SetOverdraft(account, row.OverdraftLimitMinor, now);
            }

            var television = EconomyRuleSet.PerMatchday(EconomyRuleSet.LeagueTvSeasonMinorFor(row.TierNumber));

            if (television > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.LeagueTv(Guid.CreateVersion7(), clubId, matchdayId, row.TierNumber, television),
                    now));
            }

            if (row.SponsorFixedMatchdayMinor > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.SponsorMatchday(Guid.CreateVersion7(), clubId, matchdayId, row.SponsorFixedMatchdayMinor),
                    now));
            }

            var wages = EconomyRuleSet.PerMatchday(row.SeasonWageMinor);

            if (wages > 0)
            {
                await _room.MakeRoomAsync(
                    account,
                    wages,
                    amount => LedgerPostings.MatchdayEmergencyGrant(Guid.CreateVersion7(), clubId, matchdayId, amount),
                    LedgerPostings.MatchdayMoneyCorrelationId(matchdayId, clubId),
                    now,
                    cancellationToken);

                _ledger.Add(account.Post(
                    LedgerPostings.MatchdayWages(Guid.CreateVersion7(), clubId, matchdayId, row.ContractedPlayers, wages),
                    now));
            }

            if (roundNumber == EconomyRuleSet.MatchdaysPerSeason / 2)
            {
                SetOverdraft(account, row.OverdraftLimitMinor, now);
            }
        }

        return await _states.ApplyAsync(
            clubIds,
            MatchdayOrdinal.Of(seasonSequenceNumber, roundNumber),
            $"matchday-money:{matchdayId:D}",
            now,
            cancellationToken);
    }

    /// <summary>
    /// Sets a club's overdraft limit from its forecast, never lower than the debt it already carries needs (`DEBT-1`).
    /// </summary>
    /// <param name="account">The club's account, tracked.</param>
    /// <param name="limitMinor">The limit the forecast gives.</param>
    /// <param name="now">The current instant.</param>
    public static void SetOverdraft(ClubAccount account, long limitMinor, DateTimeOffset now)
    {
        // The floor is twice the limit, so a club already owing more than the lower floor allows keeps a limit that holds its debt.
        var needed = account.CashMinor < 0 ? (-account.CashMinor + 1) / EconomyRuleSet.SafetyNetLimitMultiple : 0;
        var limit = Math.Max(limitMinor, needed);

        if (limit != account.OverdraftLimitMinor)
        {
            account.SetOverdraft(limit, now);
        }
    }

    /// <summary>Posts one gate receipt and one snack bar sale per published fixture's host club.</summary>
    /// <param name="matchdayId">The round being published, which names the division to price.</param>
    /// <param name="published">The fixtures the round published.</param>
    /// <param name="now">The current instant, taken from the caller's clock (`TIME-2`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many gate receipts were posted.</returns>
    public async Task<int> PostMatchdayTakingsAsync(
        Guid matchdayId,
        IReadOnlyList<Fixture> published,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(published);

        if (published.Count == 0)
        {
            return 0;
        }

        var basis = (await _queries.GetMatchdayRevenueBasisAsync(matchdayId, cancellationToken))
            .ToDictionary(row => row.ClubId);

        var accounts = (await _accounts.LoadAsync(
                [.. published.Select(fixture => fixture.HomeClubId).Distinct()],
                cancellationToken))
            .ToDictionary(account => account.ClubId);

        var expected = (await _fans.LoadExpectationsAsync(
                [.. published.Select(fixture => fixture.Id)],
                cancellationToken))
            .ToDictionary(expectation => (expectation.FixtureId, expectation.ClubId));

        var posted = 0;

        foreach (var fixture in published.OrderBy(fixture => fixture.Id))
        {
            var clubId = fixture.HomeClubId;

            if (!basis.TryGetValue(clubId, out var revenue))
            {
                throw new InvalidOperationException(
                    $"Fixture {fixture.Id:D} was published but its host {clubId:D} has no revenue basis (FIN-3).");
            }

            if (!accounts.TryGetValue(clubId, out var account))
            {
                throw new InvalidOperationException(
                    $"Club {clubId:D} has no account, so its gate cannot be posted (FIN-11).");
            }

            var amount = StadiumRuleSet.GateRevenueMinorFor(
                revenue.Seats,
                revenue.TierNumber,
                revenue.Fans,
                revenue.MoodBp);

            if (amount <= 0)
            {
                continue;
            }

            _ledger.Add(account.Post(
                LedgerPostings.GateReceipt(Guid.CreateVersion7(), clubId, fixture.Id, amount),
                now));

            posted++;

            // A fixture locked before the fan module existed has no frozen expectation, and is judged as an
            // even game at the ground it was played on, the same fallback the fans use (STRC-5).
            var expectedPoints = expected.TryGetValue((fixture.Id, clubId), out var frozen)
                ? frozen.ExpectedPointsCentis
                : FanRuleSet.ExpectationForGap(0, atHome: true).ExpectedPointsCentis;

            PostSnackBar(fixture, revenue, account, expectedPoints, now);
        }

        return posted;
    }

    private void PostSnackBar(
        Fixture fixture,
        ClubRevenueBasis revenue,
        ClubAccount account,
        int expectedPointsCentis,
        DateTimeOffset now)
    {
        var spectators = StadiumRuleSet.AttendanceFor(revenue.Seats, revenue.Fans, revenue.MoodBp);
        var bonus = StructureRuleSet.ResultBonusFor(
            fixture.HomeScore!.Value,
            fixture.AwayScore!.Value,
            expectedPointsCentis);

        var baseMinor = StructureRuleSet.SnackBaseMinorFor(revenue.SnackBarLevel, spectators, revenue.TierNumber);
        var amount = StructureRuleSet.WithBonus(baseMinor, bonus.Bp);

        // A ledger entry is never empty, so a crowd of nobody sells nothing and writes nothing.
        if (amount <= 0)
        {
            return;
        }

        var entryId = Guid.CreateVersion7();

        _ledger.Add(account.Post(
            LedgerPostings.SnackBarTakings(
                entryId,
                revenue.ClubId,
                fixture.Id,
                revenue.SnackBarLevel,
                spectators,
                bonus.Bp,
                amount),
            now));

        _structures.AddTakings(ClubStructureTakings.MatchSale(
            Guid.CreateVersion7(),
            revenue.ClubId,
            fixture.Id,
            WorldRuleSet.WeeklyFinanceDateFor(fixture.KickoffAt),
            revenue.SnackBarLevel,
            spectators,
            baseMinor,
            bonus.Bp,
            bonus.Reason,
            amount,
            entryId,
            now));
    }
}

using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Comms;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Finance;

/// <summary>
/// Posts a published round's gate revenue (`FIN-3`, master plan §7.4.7).
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>MatchdayNotifications</c> for money: publication calls this in the same
/// transaction that made the results public, so the gate and the result become public together, and a
/// publication that fails leaves neither behind. It stages its entries through the ledger and never saves,
/// which is what lets the caller commit them with the table it just rebuilt.
/// </para>
/// <para>
/// The gate is drawn against the rank the round produced, not the rank going in, because that is the table
/// the fixture has just changed — a club that won its way up the division the same evening draws the bigger
/// crowd for the match it played. The correlation key is the fixture, so a retried publication collides
/// with the first entry rather than drawing the gate twice (`FIN-17`).
/// </para>
/// </remarks>
public sealed class MatchdayFinances
{
    private readonly IFinanceQueries _queries;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;

    /// <summary>Initializes the collaborator.</summary>
    public MatchdayFinances(
        IFinanceQueries queries,
        IClubAccountRepository accounts,
        ILedgerRepository ledger)
    {
        _queries = queries;
        _accounts = accounts;
        _ledger = ledger;
    }

    /// <summary>Posts one gate receipt per published fixture's host club.</summary>
    /// <param name="matchdayId">The round being published, which names the division to price.</param>
    /// <param name="published">The fixtures the round published.</param>
    /// <param name="positions">Every club's position after the round, which is the form the gate draws on.</param>
    /// <param name="now">The current instant, taken from the caller's clock (`TIME-2`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many gate receipts were posted.</returns>
    public async Task<int> PostGateReceiptsAsync(
        Guid matchdayId,
        IReadOnlyList<Fixture> published,
        IReadOnlyList<ClubPositionFact> positions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(positions);

        if (published.Count == 0)
        {
            return 0;
        }

        var basis = (await _queries.GetMatchdayRevenueBasisAsync(matchdayId, cancellationToken))
            .ToDictionary(row => row.ClubId);

        var ranks = positions.ToDictionary(position => position.ClubId, position => position.Position);

        var accounts = (await _accounts.LoadAsync(
                [.. published.Select(fixture => fixture.HomeClubId).Distinct()],
                cancellationToken))
            .ToDictionary(account => account.ClubId);

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

            if (!ranks.TryGetValue(clubId, out var formRank))
            {
                throw new InvalidOperationException(
                    $"Club {clubId:D} has no position in the rebuilt table, so its gate has no form (FIN-3).");
            }

            var amount = StadiumRuleSet.GateRevenueMinorFor(revenue.Seats, revenue.TierNumber, formRank);

            if (amount <= 0)
            {
                continue;
            }

            _ledger.Add(account.Post(
                LedgerPostings.GateReceipt(Guid.CreateVersion7(), clubId, fixture.Id, amount),
                now));

            posted++;
        }

        return posted;
    }
}

using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Finance;

/// <summary>
/// One club's money as a deal is weighed against it: the forecast, the wage bill with the leading bids on it, and the cash left after them
/// (`WAGE-1`, plan toasty-scott section 5).
/// </summary>
public sealed class WageImpactClub
{
    private readonly ClubFinanceBasis _basis;
    private readonly ClubMoneyPosition _money;

    /// <summary>Initializes the club's position.</summary>
    /// <param name="basis">What the club's money is planned from.</param>
    /// <param name="money">What the club holds and has promised.</param>
    public WageImpactClub(ClubFinanceBasis basis, ClubMoneyPosition money)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(money);

        _basis = basis;
        _money = money;
    }

    /// <summary>Reads a deal against the club's money.</summary>
    /// <param name="feeMinor">The fee the deal costs now, 0 for a renewal.</param>
    /// <param name="addedSeasonWageMinor">What the deal adds to the season's wages: the whole wage of a signing, the rise of a renewal.</param>
    public WageImpactResponse Evaluate(long feeMinor, long addedSeasonWageMinor)
    {
        var result = WageImpact.Evaluate(
            new WageImpactInput(
                _money.AvailableMinor,
                feeMinor,
                addedSeasonWageMinor,
                _basis.SeasonWageMinor + _money.LeadingBidWagesMinor,
                _basis.ForecastMinor,
                EconomyRuleSet.FixedCostSeasonMinor(_basis.TierNumber, _basis.Seats, _basis.SnackBarLevel, _basis.MerchandiseLevel),
                _money.MatchdaysPlayed),
            _basis.OverdraftLimitMinor);

        return new WageImpactResponse(
            result.Level.ToCode(),
            addedSeasonWageMinor,
            result.WageBillAfterMinor,
            result.WageBudgetMinor,
            result.WageShareBp,
            result.SeasonEndCashMinor,
            result.RedFromMatchday,
            result.NeedsConfirmation);
    }
}

/// <summary>
/// Reads a club's money for the wage impact of a signing or a renewal, so the server tells the viewer what a deal would do and the client never
/// works it out itself (`INT-1`).
/// </summary>
public sealed class WageImpactReader
{
    private readonly IFinanceQueries _finance;

    /// <summary>Initializes the reader.</summary>
    public WageImpactReader(IFinanceQueries finance) => _finance = finance;

    /// <summary>Reads a club's position, or null when it has no account or no place in the season.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<WageImpactClub?> ReadAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var money = await _finance.GetMoneyPositionAsync(clubId, cancellationToken);

        if (money is null)
        {
            return null;
        }

        var bases = await _finance.GetClubFinanceBasisAsync([clubId], cancellationToken);

        return bases.Count == 0 ? null : new WageImpactClub(bases[0], money);
    }
}

using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Application.Abstractions.Finance;

/// <summary>One computer club's cash and the state of its money, as the building policy reads them.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="AvailableMinor">The cash the club can spend now: what is left after its leading bids (`FIN-10`).</param>
/// <param name="State">The state of the club's money; a club that is not healthy builds nothing (`DEBT-3`).</param>
public sealed record AiClubMoney(Guid ClubId, long AvailableMinor, FinancialStateKind State);

/// <summary>
/// The computer clubs' money reads (`ai-finance-v1`, plan toasty-scott section 7).
/// </summary>
/// <remarks>
/// Flat projections in a fixed number of queries: the clubs no human holds with their accounts, and how far into the season the world is. Nothing
/// here returns a tracked graph and nothing mutates; the policy decides, and the shared building writer carries the decision out.
/// </remarks>
public interface IAiFinanceQueries
{
    /// <summary>Loads every club no human holds that plays in the current season, with its spendable cash and the state of its money.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AiClubMoney>> LoadAiClubMoneyAsync(CancellationToken cancellationToken);

    /// <summary>Reads the latest round any division has published in the current season, 0 before the first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> GetRoundsPublishedAsync(CancellationToken cancellationToken);
}

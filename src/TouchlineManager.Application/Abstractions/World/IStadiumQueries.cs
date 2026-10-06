namespace TouchlineManager.Application.Abstractions.World;

/// <summary>What a club's stadium page needs to know about the club beside the ground itself.</summary>
/// <param name="TierNumber">The tier the club plays in this season, which scales prices and build costs.</param>
/// <param name="CashMinor">The club's cash, in minor units.</param>
/// <param name="ReservedMinor">The part of it already committed to open bids, in minor units.</param>
public sealed record ClubStadiumContext(int TierNumber, long CashMinor, long ReservedMinor);

/// <summary>
/// The stadium module's read projection (`STAD-1`).
/// </summary>
/// <remarks>
/// One lean read instead of the whole finance summary, which also sums wages and walks the season's ledger:
/// the stadium page asks only what the club can afford and which tier's prices apply.
/// </remarks>
public interface IStadiumQueries
{
    /// <summary>Reads the club's tier and money for pricing a build.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The context, or null when the club has no account.</returns>
    Task<ClubStadiumContext?> GetContextAsync(Guid clubId, CancellationToken cancellationToken);
}

using Tactical11Manager.Domain.Fans;

namespace Tactical11Manager.Application.Abstractions.Fans;

/// <summary>
/// Persistence for the fans: each club's fan base, the history of what moved it, and the expectations frozen at the lock (`FAN-10`).
/// </summary>
/// <remarks>
/// Like the other write ports it only stages: the caller saves, so a fan change commits in the transaction of the publication, the
/// rollover step or the takeover that caused it.
/// </remarks>
public interface IFanRepository
{
    /// <summary>Stages a new club's fan base.</summary>
    /// <param name="fanBase">The fan base.</param>
    void Add(ClubFanBase fanBase);

    /// <summary>Stages a change to the fan history.</summary>
    /// <param name="fanEvent">The event.</param>
    void AddEvent(ClubFanEvent fanEvent);

    /// <summary>Stages an expectation frozen at the lock.</summary>
    /// <param name="expectation">The expectation.</param>
    void AddExpectation(FixtureFanExpectation expectation);

    /// <summary>Loads several clubs' fan bases, tracked, in one round trip.</summary>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fan bases that exist, in no particular order.</returns>
    Task<IReadOnlyList<ClubFanBase>> LoadAsync(IReadOnlyCollection<Guid> clubIds, CancellationToken cancellationToken);

    /// <summary>Loads the expectations frozen for some fixtures.</summary>
    /// <param name="fixtureIds">The fixtures.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The expectations that exist, two for a fixture that was locked.</returns>
    Task<IReadOnlyList<FixtureFanExpectation>> LoadExpectationsAsync(
        IReadOnlyCollection<Guid> fixtureIds,
        CancellationToken cancellationToken);

    /// <summary>Finds the clubs whose fans were already reviewed for a week, so a repeated review leaves them alone.</summary>
    /// <param name="weekOf">The Sunday the review is for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlySet<Guid>> FindClubsReviewedAsync(DateOnly weekOf, CancellationToken cancellationToken);
}

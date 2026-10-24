using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Application.Abstractions.Competition;

/// <summary>
/// Persistence for the promotion play-off bracket: the ties a season draws and the match each is played in (`PR-4`).
/// </summary>
/// <remarks>
/// The loads return tracked ties because the pipeline moves them through their own transitions (<see cref="PlayoffTie.FillAway"/>,
/// <see cref="PlayoffTie.AttachFixture"/>, <see cref="PlayoffTie.Decide"/>) in the same transaction as the publication that
/// drives them. The two counting reads exist for the rollover, which waits until the season is settled.
/// </remarks>
public interface IPlayoffRepository
{
    /// <summary>Stages a drawn tie.</summary>
    /// <param name="tie">The tie.</param>
    void Add(PlayoffTie tie);

    /// <summary>Lists a season's ties, tracked, in kickoff slot order (semi-finals before finals) and then by league and identity.</summary>
    /// <param name="seasonId">The season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PlayoffTie>> ListBySeasonAsync(Guid seasonId, CancellationToken cancellationToken);

    /// <summary>Finds the tie a fixture is the match of, tracked.</summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tie, or null when the fixture is not a play-off match.</returns>
    Task<PlayoffTie?> FindByFixtureAsync(Guid fixtureId, CancellationToken cancellationToken);

    /// <summary>Counts a season's ties that have no winner yet.</summary>
    /// <param name="seasonId">The season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountUndecidedAsync(Guid seasonId, CancellationToken cancellationToken);

    /// <summary>
    /// Tells whether a season has nothing left to play: every matchday of every league is published, play-off rounds
    /// included, and no tie is undecided.
    /// </summary>
    /// <param name="seasonId">The season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsSeasonSettledAsync(Guid seasonId, CancellationToken cancellationToken);
}

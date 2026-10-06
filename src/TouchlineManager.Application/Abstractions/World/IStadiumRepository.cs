using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Abstractions.World;

/// <summary>
/// Persistence for club stadiums (`STAD-1`).
/// </summary>
/// <remarks>
/// The world module owns <c>world.club_stadiums</c>, so every write to it goes through here (`MOD-1`). The
/// gate-revenue read is a projection in the finance queries, not a method on this port, for the reason the
/// module rules give (`MOD-3`).
/// </remarks>
public interface IStadiumRepository
{
    /// <summary>Stages a newly generated club's ground.</summary>
    void Add(ClubStadium stadium);

    /// <summary>Finds a club's ground, tracked, or null when none has been opened.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClubStadium?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken);
}

using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Application.Ops;

/// <summary>Finds the season the stepped clock's end-of-season step closes (ADR-0049).</summary>
internal static class ClockSeason
{
    /// <summary>Gets the world's current season when it is still active, or null when there is no world or the season has closed.</summary>
    /// <param name="world">The world repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Season?> FindActiveAsync(IWorldRepository world, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);

        var current = await world.FindWorldAsync(cancellationToken);

        if (current is null)
        {
            return null;
        }

        var season = await world.FindSeasonAsync(current.Id, current.CurrentSeasonNumber, cancellationToken);

        return season is { Status: SeasonStatus.Active } ? season : null;
    }
}

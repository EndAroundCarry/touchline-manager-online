using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Fans;

/// <summary>
/// What the season rollover does to the fans: the last verdict and the move back toward calm when a season closes, and the tier jump and
/// the new target when the next one opens (`FAN-7`, `FAN-8`).
/// </summary>
/// <remarks>
/// Both halves run inside the rollover phase that owns them, in the same transaction as the phase's checkpoint, so a phase that is
/// retried finds its work done or not started and cannot apply a season twice. Nothing here saves.
/// </remarks>
public sealed class SettleSeasonFans
{
    private readonly IFanQueries _queries;
    private readonly FanBaseKeeper _keeper;

    /// <summary>Initializes the collaborator.</summary>
    public SettleSeasonFans(IFanQueries queries, FanBaseKeeper keeper)
    {
        _queries = queries;
        _keeper = keeper;
    }

    /// <summary>Closes a season for every club's fans: the verdict on the target, the move halfway back to calm, and the slow fall of a crowd the division cannot hold.</summary>
    /// <param name="seasonId">The season that is closing.</param>
    /// <param name="clubs">Each club with the tier it played in and where it finished.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many clubs' fans were closed.</returns>
    public async Task<int> CloseSeasonAsync(
        Guid seasonId,
        IReadOnlyList<SeasonSettlementClub> clubs,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clubs);

        if (clubs.Count == 0)
        {
            return 0;
        }

        var tierOf = clubs.ToDictionary(club => club.ClubId, club => club.TierNumber);
        var bases = await _keeper.LoadAsync([.. tierOf.Keys], clubId => tierOf[clubId], seasonId, now, cancellationToken);

        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            var fanBase = bases[club.ClubId];
            int? gap = fanBase.SeasonTargetRank is { } target ? club.FinalRank - target : null;
            var after = FanRuleSet.AfterSeason(fanBase.State, club.TierNumber, gap);

            _keeper.Change(fanBase, after, FanEventKind.SeasonReset, seasonId, now, gap: gap);
        }

        return clubs.Count;
    }

    /// <summary>Opens a season for the clubs placed in one tier: the jump for a club that changed tier, and the place its fans hope for.</summary>
    /// <param name="nextSeasonId">The season that is opening.</param>
    /// <param name="clubIds">The clubs placed in the tier.</param>
    /// <param name="moved">The clubs whose tier changed this rollover, with where they came from.</param>
    /// <param name="tier">The tier the clubs are placed in.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many clubs' fans were opened for the season.</returns>
    public async Task<int> OpenSeasonAsync(
        Guid nextSeasonId,
        IReadOnlyList<Guid> clubIds,
        IReadOnlyDictionary<Guid, ClubMovement> moved,
        int tier,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        ArgumentNullException.ThrowIfNull(moved);

        if (clubIds.Count == 0)
        {
            return 0;
        }

        var bases = await _keeper.LoadAsync(clubIds, _ => tier, nextSeasonId, now, cancellationToken);
        var ranks = FanBaseKeeper.RankByStrength(await _queries.GetSquadStrengthsAsync(clubIds, cancellationToken));

        foreach (var clubId in clubIds.Order())
        {
            var fanBase = bases[clubId];

            if (moved.TryGetValue(clubId, out var movement) && !movement.IsStationary)
            {
                _keeper.Change(
                    fanBase,
                    FanRuleSet.OnTierChange(fanBase.State, movement.FromTier, movement.ToTier),
                    movement.IsPromoted ? FanEventKind.Promotion : FanEventKind.Relegation,
                    nextSeasonId,
                    now);
            }

            fanBase.SetTarget(ranks[clubId], now);
        }

        return clubIds.Count;
    }
}

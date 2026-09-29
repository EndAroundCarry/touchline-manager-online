using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Competition;

/// <summary>One active tier's final ordering, as promotion and relegation reads it (`PR-1`).</summary>
/// <param name="TierNumber">The tier's number, ascending down the pyramid.</param>
/// <param name="RankedClubIds">The tier's clubs, best first, exactly as the finalized table ranks them (`TBL-12`).</param>
public sealed record TierStandings(int TierNumber, IReadOnlyList<Guid> RankedClubIds);

/// <summary>One club's movement between two seasons (`PR-3`).</summary>
/// <remarks>
/// Movement applies to the <em>club</em>, not to the manager, so a human manager keeps their club and
/// travels with it (`PR-3`). A club that neither goes up nor down still carries a movement record with
/// <see cref="FromTier"/> equal to <see cref="ToTier"/>, so every club has exactly one placement.
/// </remarks>
/// <param name="ClubId">The club.</param>
/// <param name="FromTier">The tier it played in.</param>
/// <param name="ToTier">The tier it plays in next.</param>
/// <param name="IsPromoted">Whether it went up.</param>
/// <param name="IsRelegated">Whether it went down.</param>
public sealed record ClubMovement(Guid ClubId, int FromTier, int ToTier, bool IsPromoted, bool IsRelegated)
{
    /// <summary>Gets a value indicating whether the club stays in its tier.</summary>
    public bool IsStationary => !IsPromoted && !IsRelegated;
}

/// <summary>
/// The promotion and relegation rules as one pure function (`PR-1`…`PR-10`).
/// </summary>
/// <remarks>
/// <para>
/// Three up and three down between every adjacent pair of active tiers: the top three of a lower tier
/// climb, the bottom three of the higher tier drop. Only active tiers take part, so a tier being generated
/// is not moved, and a tier provisioned during the closing season is active by rollover and therefore
/// takes part in it (`PR-8`).
/// </para>
/// <para>
/// The top active tier promotes nobody, because its clubs are already in the highest tier that exists, and
/// the lowest active tier relegates nobody, because there is no tier below it to drop into (`PR-2`, `PR-9`,
/// `PR-10`). A country with a single active tier therefore moves nobody at all, and both of those cases
/// fall out of the loop over adjacent pairs rather than needing their own branch.
/// </para>
/// <para>
/// Nothing here reads a clock, a database, or a random source. The input is the finalized ordering the
/// standings projection already produced, so the movement a finished season earns is reproducible from
/// what was stored.
/// </para>
/// </remarks>
public static class PromotionRelegation
{
    /// <summary>The version of the movement rule, so a change in how clubs move is a named change.</summary>
    public const string Version = "promotion-relegation-v1";

    /// <summary>Computes every club's movement between two seasons.</summary>
    /// <param name="tiers">
    /// A country's active tiers, each with its clubs in final order. Any order is accepted; the result is
    /// the same whatever order they arrive in, because the movement is computed per adjacent pair.
    /// </param>
    /// <returns>One movement per club, in ascending destination tier then descending origin tier order.</returns>
    public static IReadOnlyList<ClubMovement> Compute(IReadOnlyList<TierStandings> tiers)
    {
        ArgumentNullException.ThrowIfNull(tiers);

        if (tiers.Count == 0)
        {
            return [];
        }

        var ordered = tiers.OrderBy(tier => tier.TierNumber).ToList();
        var placements = new Dictionary<Guid, ClubMovement>();
        var originTierOf = new Dictionary<Guid, int>();

        foreach (var tier in ordered)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(tier.TierNumber, 1);

            if (tier.RankedClubIds.Count == 0)
            {
                throw new ArgumentException(
                    $"Tier {tier.TierNumber} has no clubs to move.",
                    nameof(tiers));
            }

            foreach (var clubId in tier.RankedClubIds)
            {
                if (!originTierOf.TryAdd(clubId, tier.TierNumber))
                {
                    throw new ArgumentException(
                        $"Club {clubId} appears in more than one tier; a club has exactly one placement (PR-3).",
                        nameof(tiers));
                }

                placements[clubId] = new ClubMovement(clubId, tier.TierNumber, tier.TierNumber, false, false);
            }
        }

        // Each adjacent pair of active tiers exchanges three clubs each way. A club can only move one tier
        // because a tier's top three can only go the one way and its bottom three the other, and no pair
        // shares a club.
        for (var index = 0; index + 1 < ordered.Count; index++)
        {
            var upper = ordered[index];
            var lower = ordered[index + 1];

            if (lower.RankedClubIds.Count < WorldRuleSet.RelegatedPerTier
                || upper.RankedClubIds.Count < WorldRuleSet.PromotedPerTier)
            {
                throw new ArgumentException(
                    $"Tiers {upper.TierNumber} and {lower.TierNumber} do not each hold "
                    + $"{WorldRuleSet.PromotedPerTier} clubs to exchange (PR-1).",
                    nameof(tiers));
            }

            foreach (var clubId in lower.RankedClubIds.Take(WorldRuleSet.PromotedPerTier))
            {
                if (placements[clubId].IsRelegated)
                {
                    throw new InvalidOperationException($"Club {clubId} would be both promoted and relegated.");
                }

                placements[clubId] = new ClubMovement(clubId, lower.TierNumber, upper.TierNumber, true, false);
            }

            foreach (var clubId in upper.RankedClubIds.TakeLast(WorldRuleSet.RelegatedPerTier))
            {
                if (placements[clubId].IsPromoted)
                {
                    throw new InvalidOperationException($"Club {clubId} would be both promoted and relegated.");
                }

                placements[clubId] = new ClubMovement(clubId, upper.TierNumber, lower.TierNumber, false, true);
            }
        }

        return placements.Values
            .OrderBy(movement => movement.ToTier)
            .ThenByDescending(movement => movement.FromTier)
            .ThenBy(movement => movement.ClubId)
            .ToList();
    }
}

using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>The versioned identity of the AI contract-continuity policy (`CON-6`, `CON-8`, `FIC-8`).</summary>
/// <remarks>
/// A changed renewal target, ordering, or term rule is a new policy version, exactly as a changed selection
/// order is for the club policy: it is stamped into the audit trail so a season's squad movements are
/// reproducible from the label.
/// </remarks>
public static class AiContractPolicyVersions
{
    /// <summary>The current policy label.</summary>
    public const string Version = "ai-contract-v1";
}

/// <summary>One expiring player as the AI contract policy weighs them (`CON-6`).</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Ability">The player's ability, on the 1–20 scale.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="IsGoalkeeper">Whether the player counts against the goalkeeper minimum (`SQ-2`).</param>
public sealed record AiContractPlayer(Guid PlayerId, int Ability, int Age, bool IsGoalkeeper);

/// <summary>The policy's verdict on one expiring player.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Renew">Whether the club keeps the player.</param>
/// <param name="Seasons">The term to renew for, or zero when released (`CON-1`).</param>
public sealed record AiContractDecision(Guid PlayerId, bool Renew, int Seasons);

/// <summary>
/// The deterministic policy a club nobody manages applies to its expiring contracts at rollover
/// (`CON-6`, `CON-8`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the squad's shape, so an unmanaged club's squad is stable across replays and a
/// seeded world's AI clubs keep a legal, sensible size without a human. It renews the best expiring
/// players up to the AI's target size, then extends renewals only as far as legality requires — the
/// minimum registered squad and the goalkeeper minimum (`SQ-2`) — and lets the rest go to free agency.
/// </para>
/// <para>
/// It invents no privilege: it renews on the same wage scale and term bounds a manager's renewal uses, and
/// it never renews a player past the retirement rule's reach. `PlayerId` breaks ties so the decision does
/// not depend on the order the club's squad happened to be loaded in.
/// </para>
/// </remarks>
public static class AiContractPolicy
{
    /// <summary>Decides which of a club's expiring players are renewed, and for how long.</summary>
    /// <param name="stableCount">How many players the club holds whose contracts are not expiring.</param>
    /// <param name="stableGoalkeepers">How many of those are goalkeepers.</param>
    /// <param name="expiring">The players whose contracts run out this rollover.</param>
    /// <returns>One decision per expiring player, ordered by player id.</returns>
    public static IReadOnlyList<AiContractDecision> Decide(
        int stableCount,
        int stableGoalkeepers,
        IReadOnlyList<AiContractPlayer> expiring)
    {
        ArgumentNullException.ThrowIfNull(expiring);
        ArgumentOutOfRangeException.ThrowIfNegative(stableCount);
        ArgumentOutOfRangeException.ThrowIfNegative(stableGoalkeepers);

        var candidates = expiring
            .OrderByDescending(player => player.Ability)
            .ThenBy(player => player.PlayerId)
            .ToList();

        var renewed = new Dictionary<Guid, int>(candidates.Count);
        var total = stableCount;
        var goalkeepers = stableGoalkeepers;

        // Renew the best up to the AI target size.
        foreach (var player in candidates)
        {
            if (total >= WorldRuleSet.AiContractTargetSquadSize)
            {
                break;
            }

            renewed[player.PlayerId] = SeasonsFor(player.Age);
            total++;

            if (player.IsGoalkeeper)
            {
                goalkeepers++;
            }
        }

        // Then renew only as far as the legal floor requires: a goalkeeper first when the goalkeeper minimum
        // is short, otherwise the best remaining player.
        if (total < WorldRuleSet.SquadMinimumRegistered || goalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            var remaining = candidates
                .Where(player => !renewed.ContainsKey(player.PlayerId))
                .OrderByDescending(player => goalkeepers < WorldRuleSet.MinimumGoalkeepers && player.IsGoalkeeper)
                .ThenByDescending(player => player.Ability)
                .ThenBy(player => player.PlayerId);

            foreach (var player in remaining)
            {
                if (total >= WorldRuleSet.SquadMinimumRegistered && goalkeepers >= WorldRuleSet.MinimumGoalkeepers)
                {
                    break;
                }

                renewed[player.PlayerId] = SeasonsFor(player.Age);
                total++;

                if (player.IsGoalkeeper)
                {
                    goalkeepers++;
                }
            }
        }

        return
        [
            .. expiring
                .OrderBy(player => player.PlayerId)
                .Select(player => renewed.TryGetValue(player.PlayerId, out var seasons)
                    ? new AiContractDecision(player.PlayerId, Renew: true, seasons)
                    : new AiContractDecision(player.PlayerId, Renew: false, Seasons: 0)),
        ];
    }

    /// <summary>The term the AI offers a player of the given age, within the legal 1–3 seasons (`CON-1`).</summary>
    private static int SeasonsFor(int age) => age switch
    {
        <= 23 => 3,
        <= 28 => 2,
        _ => 1,
    };
}

using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Randomness;
using Tactical11Manager.MatchEngine.Ratings;
using Tactical11Manager.MatchEngine.Simulation;

namespace Tactical11Manager.MatchEngine.Shootout;

/// <summary>
/// Decides a drawn knock-out match on penalties, outside the match simulation.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic: the same seed and the same two sides always give the same kicks. It is deliberately not part
/// of <see cref="MatchSimulator"/>, so a league match, the engine version, the formula hash and the golden hashes are
/// all untouched (ADR-0004); the caller runs it after the match when, and only when, a play-off ends level.
/// </para>
/// <para>
/// Each kick uses the chance a penalty in open play has (<see cref="SetPieceDirector.PenaltyGoalChance"/>): the taker's
/// finishing and composure against the goalkeeper's reflexes, inside the engine's bounds. The rolls come from a stream
/// of their own, so they never coincide with the match's.
/// </para>
/// <para>
/// A side's takers go in order of finishing, best first and then by identity, with the goalkeeper last. Each side
/// kicks five times, stopping early once the other cannot catch up; after that it is sudden death, cycling round the
/// same order. A side with fewer players on the pitch makes the other drop its weakest takers to the same number.
/// </para>
/// </remarks>
public static class PenaltyShootout
{
    /// <summary>The version of the shoot-out rules, stored with a decided shoot-out.</summary>
    public const string Version = "shootout-v1";

    /// <summary>The generator stream the shoot-out draws from, apart from every stream the match uses.</summary>
    public const ulong ShootoutStream = 0x5348_4F4F_544F_5554UL;

    /// <summary>The most rounds a shoot-out may run before a coin settles it, which only a broken input could reach.</summary>
    private const int RoundCeiling = 1_000;

    /// <summary>Runs a shoot-out.</summary>
    /// <param name="seed">The match's seed; the shoot-out draws from its own stream of it.</param>
    /// <param name="home">The home side's players on the pitch.</param>
    /// <param name="away">The away side's players on the pitch.</param>
    /// <param name="regulationKicks">How many kicks each side takes before sudden death.</param>
    /// <param name="rules">The rules in force.</param>
    /// <returns>The decided shoot-out.</returns>
    public static ShootoutResult Run(
        ulong seed,
        ShootoutSide home,
        ShootoutSide away,
        int regulationKicks,
        EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(away);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfLessThan(regulationKicks, 1);

        if (home.OnPitch.Count == 0 || away.OnPitch.Count == 0)
        {
            throw new InvalidMatchInputException("A shoot-out needs players on the pitch for both sides.");
        }

        var random = new Pcg32(seed, ShootoutStream);

        var first = random.NextInt(2) == 0 ? MatchSide.Home : MatchSide.Away;
        var second = first == MatchSide.Home ? MatchSide.Away : MatchSide.Home;

        var homeKeeper = KeeperOf(home);
        var awayKeeper = KeeperOf(away);

        var takers = Math.Min(home.OnPitch.Count, away.OnPitch.Count);
        var homeOrder = TakingOrder(home, takers, rules);
        var awayOrder = TakingOrder(away, takers, rules);

        var kicks = new List<ShootoutKick>();
        var scored = new int[2];
        var taken = new int[2];

        for (var round = 1; round <= RoundCeiling; round++)
        {
            foreach (var side in new[] { first, second })
            {
                var order = side == MatchSide.Home ? homeOrder : awayOrder;
                var keeper = side == MatchSide.Home ? awayKeeper : homeKeeper;
                var taker = order[(round - 1) % order.Count];

                var outcome = Kick(taker, keeper, rules, random);

                kicks.Add(new ShootoutKick(
                    kicks.Count + 1,
                    round,
                    side,
                    taker.Participant.ParticipantId,
                    keeper?.Participant.ParticipantId,
                    outcome));

                taken[(int)side]++;

                if (outcome == ShootoutKickOutcome.Scored)
                {
                    scored[(int)side]++;
                }

                if (IsDecided(scored, taken, regulationKicks))
                {
                    return Finish(first, scored, kicks);
                }
            }
        }

        // Only reachable if every round of a thousand ended level, which the bounds on a penalty's chance make
        // impossible in practice. A coin from the same stream still keeps the answer deterministic.
        if (random.NextInt(2) == 0)
        {
            scored[(int)MatchSide.Home]++;
        }
        else
        {
            scored[(int)MatchSide.Away]++;
        }

        return Finish(first, scored, kicks);
    }

    private static ShootoutResult Finish(MatchSide first, int[] scored, List<ShootoutKick> kicks) =>
        new(Version, first, scored[(int)MatchSide.Home], scored[(int)MatchSide.Away], kicks);

    private static ActiveSlot? KeeperOf(ShootoutSide side) =>
        side.OnPitch.FirstOrDefault(slot => slot.Participant.IsGoalkeeper);

    private static ShootoutKickOutcome Kick(ActiveSlot taker, ActiveSlot? keeper, EngineRulesV2 rules, Pcg32 random)
    {
        var chance = SetPieceDirector.PenaltyGoalChance(taker, keeper, rules);

        if (random.RollBasisPoints(chance))
        {
            return ShootoutKickOutcome.Scored;
        }

        // The draw is taken whether or not there is a keeper to save it, so a side without one does not shift every
        // later roll.
        var share = random.NextBasisPoints();

        return keeper is not null && share < rules.PenaltySavedShareBasisPoints
            ? ShootoutKickOutcome.Saved
            : ShootoutKickOutcome.Wide;
    }

    /// <summary>
    /// Puts a side's takers in order, best finisher first and the goalkeeper last, and cuts the list to the number
    /// both sides can field.
    /// </summary>
    private static List<ActiveSlot> TakingOrder(ShootoutSide side, int count, EngineRulesV2 rules) =>
    [
        .. side.OnPitch
            .OrderBy(slot => slot.Participant.IsGoalkeeper)
            .ThenByDescending(slot => EffectiveSkill.Hundredths(slot, MatchAttributeName.Finishing, rules))
            .ThenBy(slot => slot.Participant.ParticipantId)
            .Take(count),
    ];

    /// <summary>
    /// Whether the shoot-out is over: one side cannot be caught within the kicks it has left, or sudden death has
    /// completed a round with the sides apart.
    /// </summary>
    private static bool IsDecided(int[] scored, int[] taken, int regulationKicks)
    {
        var home = (int)MatchSide.Home;
        var away = (int)MatchSide.Away;

        if (taken[home] >= regulationKicks && taken[away] >= regulationKicks)
        {
            return taken[home] == taken[away] && scored[home] != scored[away];
        }

        var homeLeft = regulationKicks - taken[home];
        var awayLeft = regulationKicks - taken[away];

        return scored[home] > scored[away] + awayLeft || scored[away] > scored[home] + homeLeft;
    }
}

using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The contract the AI applies to an unmanaged club's expiring players at rollover (`CON-6`, `CON-8`).
/// </summary>
public sealed class AiContractPolicyTests
{
    [Fact]
    public void The_best_expiring_players_are_renewed_first()
    {
        var best = Player(ability: 19);
        var other = Player(ability: 11);

        var decisions = AiContractPolicy.Decide(
            stableCount: WorldRuleSet.AiContractTargetSquadSize - 1,
            stableGoalkeepers: 3,
            [best, other]);

        var renewed = decisions.Single(decision => decision.Renew);

        renewed.PlayerId.Should().Be(best.PlayerId, "the club keeps its best player up to the target size");
        decisions.Single(decision => decision.PlayerId == other.PlayerId).Renew.Should().BeFalse();
    }

    [Fact]
    public void A_club_left_below_the_minimum_is_topped_up_to_legality()
    {
        // Fifteen stable players with one goalkeeper, then eight expiring of whom only some are kept.
        var expiring = new List<AiContractPlayer>
        {
            Player(ability: 15, isGoalkeeper: true),
            Player(ability: 14, isGoalkeeper: true),
        };

        expiring.AddRange(Enumerable.Range(0, 6).Select(index => Player(ability: 16 - index)));

        var decisions = AiContractPolicy.Decide(stableCount: 15, stableGoalkeepers: 1, expiring);

        var kept = decisions.Count(decision => decision.Renew);
        (15 + kept).Should().BeGreaterThanOrEqualTo(WorldRuleSet.SquadMinimumRegistered);

        var keptGoalkeepers = decisions
            .Where(decision => decision.Renew)
            .Count(decision => expiring.Single(player => player.PlayerId == decision.PlayerId).IsGoalkeeper);

        (1 + keptGoalkeepers).Should().BeGreaterThanOrEqualTo(WorldRuleSet.MinimumGoalkeepers);
    }

    [Fact]
    public void A_full_squad_releases_its_surplus()
    {
        var expiring = new[] { Player(20), Player(19), Player(18), Player(17) };

        var decisions = AiContractPolicy.Decide(stableCount: 22, stableGoalkeepers: 3, expiring);

        decisions.Should().OnlyContain(decision => !decision.Renew);
    }

    [Fact]
    public void The_term_shortens_with_age()
    {
        var young = Player(ability: 15, age: 21);
        var peak = Player(ability: 15, age: 27);
        var veteran = Player(ability: 15, age: 32);

        var decisions = AiContractPolicy
            .Decide(stableCount: 0, stableGoalkeepers: 0, [young, peak, veteran])
            .ToDictionary(decision => decision.PlayerId);

        decisions[young.PlayerId].Seasons.Should().Be(3);
        decisions[peak.PlayerId].Seasons.Should().Be(2);
        decisions[veteran.PlayerId].Seasons.Should().Be(1);
    }

    [Fact]
    public void The_decision_does_not_depend_on_the_order_the_squad_was_read_in()
    {
        var players = new[] { Player(18), Player(14), Player(16), Player(12) };

        var forward = AiContractPolicy.Decide(stableCount: 18, stableGoalkeepers: 2, players);
        var reversed = AiContractPolicy.Decide(stableCount: 18, stableGoalkeepers: 2, [.. players.Reverse()]);

        reversed.Should().BeEquivalentTo(forward);
    }

    private static AiContractPlayer Player(int ability, int age = 25, bool isGoalkeeper = false) =>
        new(Guid.CreateVersion7(), ability, age, isGoalkeeper);
}

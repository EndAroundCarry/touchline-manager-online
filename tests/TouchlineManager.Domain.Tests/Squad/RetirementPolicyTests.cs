using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The hidden announce-then-play retirement rule (`CON-6`).
/// </summary>
/// <remarks>
/// The rule is deliberately secret in its numbers, so these tests pin the behaviour a manager can observe —
/// no announcement before the start age, an announcement plays one more season, and the forced caps — and the
/// shape of the bounded chance the rule uses, rather than any single draw.
/// </remarks>
public sealed class RetirementPolicyTests
{
    private static readonly Guid SeasonId = Guid.CreateVersion7();

    [Fact]
    public void An_already_announced_player_retires()
    {
        var decision = Decide(age: 33, alreadyAnnounced: true);

        decision.Should().Be(RetirementDecision.Retire);
    }

    [Fact]
    public void A_player_below_the_start_age_never_announces()
    {
        for (var age = WorldRuleSet.PlayerMinimumAge; age < WorldRuleSet.RetirementAnnouncementStartAge; age++)
        {
            Decide(age).Should().Be(RetirementDecision.Continue, "no announcement is possible before {0}", age);
        }
    }

    [Theory]
    [InlineData(36, false)]
    [InlineData(38, true)]
    public void A_player_at_the_forced_announcement_age_announces(int age, bool isGoalkeeper)
    {
        Decide(age, isGoalkeeper).Should().Be(RetirementDecision.Announce);
    }

    [Theory]
    [InlineData(37, false)]
    [InlineData(39, true)]
    public void A_player_at_the_forced_age_retires(int age, bool isGoalkeeper)
    {
        Decide(age, isGoalkeeper).Should().Be(RetirementDecision.Retire);
    }

    [Fact]
    public void A_goalkeeper_reaches_the_cap_later_than_an_outfielder()
    {
        RetirementPolicy.ForcedAnnouncementAge(isGoalkeeper: true)
            .Should().BeGreaterThan(RetirementPolicy.ForcedAnnouncementAge(isGoalkeeper: false));
        RetirementPolicy.ForcedAge(isGoalkeeper: true)
            .Should().BeGreaterThan(RetirementPolicy.ForcedAge(isGoalkeeper: false));
    }

    [Fact]
    public void A_very_good_fit_player_is_less_likely_to_announce_than_a_weak_tired_one()
    {
        var strong = WorldRuleSet.RetirementAnnouncementChancePerMille(age: 33, ability: 18, conditionBp: 9_000);
        var weak = WorldRuleSet.RetirementAnnouncementChancePerMille(age: 33, ability: 7, conditionBp: 2_000);

        strong.Should().BeLessThan(weak);
    }

    [Fact]
    public void The_chance_rises_with_age()
    {
        var at32 = WorldRuleSet.RetirementAnnouncementChancePerMille(32, 12, 6_000);
        var at34 = WorldRuleSet.RetirementAnnouncementChancePerMille(34, 12, 6_000);

        at32.Should().BeGreaterThan(0);
        at34.Should().BeGreaterThan(at32);
        WorldRuleSet.RetirementAnnouncementChancePerMille(31, 12, 6_000).Should().Be(0);
    }

    [Fact]
    public void The_same_player_and_season_produce_the_same_decision()
    {
        var playerId = Guid.CreateVersion7();

        var first = RetirementPolicy.Decide(SeasonId, playerId, Input(age: 33));
        var second = RetirementPolicy.Decide(SeasonId, playerId, Input(age: 33));

        second.Should().Be(first);
    }

    [Fact]
    public void A_weaker_squad_announces_more_often_than_a_stronger_one()
    {
        // The gate is what makes only very good, very fit players reach the cap; over many players the
        // difference has to show, whatever any single draw does.
        var weak = CountAnnouncements(ability: 7, conditionBp: 2_000);
        var strong = CountAnnouncements(ability: 18, conditionBp: 9_000);

        weak.Should().BeGreaterThan(strong);
        strong.Should().BeLessThan(200, "a strong, fit player mostly plays on");
    }

    private static int CountAnnouncements(int ability, int conditionBp)
    {
        var count = 0;

        for (var index = 0; index < 400; index++)
        {
            var decision = RetirementPolicy.Decide(
                SeasonId,
                Guid.CreateVersion7(),
                new RetirementInput(33, IsGoalkeeper: false, ability, conditionBp, AlreadyAnnounced: false));

            if (decision == RetirementDecision.Announce)
            {
                count++;
            }
        }

        return count;
    }

    private static RetirementDecision Decide(
        int age,
        bool isGoalkeeper = false,
        bool alreadyAnnounced = false) =>
        RetirementPolicy.Decide(
            SeasonId,
            Guid.CreateVersion7(),
            new RetirementInput(age, isGoalkeeper, Ability: 12, ConditionBp: 6_000, alreadyAnnounced));

    private static RetirementInput Input(int age) =>
        new(age, IsGoalkeeper: false, Ability: 12, ConditionBp: 6_000, AlreadyAnnounced: false);
}

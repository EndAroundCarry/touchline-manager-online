using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The Stage 7 calibration bands, measured over a sample that runs in CI.
/// </summary>
/// <remarks>
/// <para>
/// The full ten-thousand-fixture calibration lives in the simulation laboratory
/// (<c>tools/simulation-benchmarks</c>, the <c>calibration</c> mode), which prints every band and the ability
/// sweep. This is the smaller version that runs with the unit tests: five thousand simulations is a few
/// seconds, and it is enough to catch a constant that moved the win shares by more than a few points.
/// </para>
/// <para>
/// The bands are wider than the laboratory's targets on purpose. A test that failed on sampling noise would
/// be turned off within a week, and a test that is turned off catches nothing — so these are built to fail on
/// a regression and not on a lucky seed.
/// </para>
/// </remarks>
public sealed class CalibrationTests
{
    private const int Fixtures = 1_500;

    [Fact]
    public void Home_advantage_is_worth_about_four_points_of_win_share()
    {
        // The counterfactual is the same fixture with the crowd neutralised: home advantage is a multiplier
        // on the home side's ratings, and the snapshot must carry the hash of the rules it is played under.
        var neutralRules = EngineRulesV2.Default with
        {
            HomeAdvantageBasisPoints = EngineRulesV2.Certain,
        };
        var neutralHash = EngineConfiguration.HashOf(neutralRules);

        var withCrowd = 0;
        var neutral = 0;

        for (var index = 0; index < Fixtures; index++)
        {
            var seed = 10_000UL + (ulong)index;

            var crowd = MatchSimulator.Simulate(TestMatchFactory.Even(seed));
            var quiet = MatchSimulator.Simulate(
                TestMatchFactory.Even(seed) with
                {
                    HomeAdvantageBasisPoints = neutralRules.HomeAdvantageBasisPoints,
                    FormulaConfigurationHash = neutralHash,
                },
                neutralRules);

            withCrowd += crowd.HomeGoals > crowd.AwayGoals ? 1 : 0;
            neutral += quiet.HomeGoals > quiet.AwayGoals ? 1 : 0;
        }

        var boost = 100.0 * (withCrowd - neutral) / Fixtures;

        // The laboratory pins the figure at a little over four points over ten thousand fixtures; this
        // sample only has to catch a regression that removed the crowd or doubled it.
        boost.Should().BeGreaterThan(0.0, "the crowd is worth something (master plan §8.5)");
        boost.Should().BeLessThan(8.0, "and it is small: it must not decide a division on its own");
    }

    [Fact]
    public void A_three_point_favourite_is_upset_about_once_in_six()
    {
        // Three attribute points is a clear tier of difference — a good side against a poor one — and it is
        // where the plan's "about 15%" underdog figure sits on this engine's measured curve. The fixture is
        // played both ways so home advantage cannot decide which side counts as the underdog.
        var weakWins = 0;
        var strongWins = 0;

        for (var index = 0; index < Fixtures; index++)
        {
            var seed = 20_000UL + (ulong)index;

            var strongAtHome = MatchSimulator.Simulate(
                TestMatchFactory.Build(seed, 16, 13, new MatchInstructionsV1()));
            var strongAway = MatchSimulator.Simulate(
                TestMatchFactory.Build(seed, 13, 16, new MatchInstructionsV1()));

            weakWins += strongAtHome.AwayGoals > strongAtHome.HomeGoals ? 1 : 0;
            weakWins += strongAway.HomeGoals > strongAway.AwayGoals ? 1 : 0;
            strongWins += strongAtHome.HomeGoals > strongAtHome.AwayGoals ? 1 : 0;
            strongWins += strongAway.AwayGoals > strongAway.HomeGoals ? 1 : 0;
        }

        var upsetShare = 100.0 * weakWins / (2.0 * Fixtures);

        upsetShare.Should().BeInRange(9.0, 24.0, "the underdog wins some and not many");
        strongWins.Should().BeGreaterThan(
            weakWins * 3,
            "ability and tactics still decide the overwhelming share of matches");
    }
}

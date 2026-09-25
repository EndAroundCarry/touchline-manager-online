using FluentAssertions;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The statistical ranges the engine is calibrated to, checked in CI so a formula change is caught here.
/// </summary>
/// <remarks>
/// <para>
/// The full distribution suite lives in the simulation laboratory
/// (<c>tools/simulation-benchmarks</c>), which runs tens of thousands of matches and prints every band. This is
/// the smaller version that runs with the unit tests: two thousand matches is a couple of seconds and is
/// enough to catch a change that moved the goals-per-match figure by a fifth.
/// </para>
/// <para>
/// The bands here are wider than the laboratory's targets on purpose. A test that failed on sampling noise
/// would be turned off within a week, and a test that is turned off catches nothing — so these are built to
/// fail on a regression and not on a lucky seed.
/// </para>
/// </remarks>
public sealed class DistributionTests
{
    private const int Matches = 2_000;

    [Fact]
    public void The_scoreline_and_discipline_distributions_stay_inside_their_ranges()
    {
        var totals = Measure();

        totals.GoalsPerMatch.Should().BeInRange(2.30, 3.40);
        totals.HomeWinShare.Should().BeInRange(0.34, 0.53);
        totals.DrawShare.Should().BeInRange(0.17, 0.33);
        totals.AwayWinShare.Should().BeInRange(0.22, 0.38);
        totals.ShotsPerMatch.Should().BeInRange(20.0, 38.0);
        totals.HomePossessionShare.Should().BeInRange(0.49, 0.56);
        totals.FoulsPerMatch.Should().BeInRange(16.0, 27.0);
        totals.YellowsPerMatch.Should().BeInRange(2.0, 5.5);
        totals.RedsPerMatch.Should().BeInRange(0.0, 0.55);
        totals.InjuriesPerMatch.Should().BeInRange(0.10, 0.80);
        totals.PenaltiesPerMatch.Should().BeInRange(0.05, 0.50);
        totals.SubstitutionsPerMatch.Should().BeInRange(3.0, 11.0);
    }

    [Fact]
    public void The_score_tail_is_not_pathological()
    {
        // A mean can sit perfectly while one match in a hundred finishes 9-3. The tail is what a manager
        // screenshots, so it is measured rather than assumed.
        var totals = Measure();

        totals.HighScoringShare.Should().BeLessThan(0.06, "seven-goal matches are rare in football too");
        totals.HighestTotalGoals.Should().BeLessThan(16);
    }

    [Fact]
    public void Home_advantage_is_real_and_small()
    {
        var totals = Measure();

        totals.HomeWinShare.Should().BeGreaterThan(
            totals.AwayWinShare,
            "home advantage exists (master plan §8.5)");

        (totals.HomeWinShare - totals.AwayWinShare).Should().BeLessThan(
            0.20,
            "and it is small: it must not decide a division on its own");
    }

    [Fact]
    public void A_better_side_wins_more_often_than_a_worse_one()
    {
        // The engine's whole purpose. If ability did not move results, every distribution above would still look
        // healthy and the game would be pointless.
        var strongHomeWins = 0;
        var strongAwayWins = 0;

        for (var seed = 1UL; seed <= 400; seed++)
        {
            var strongAtHome = TestMatchFactory.Build(seed, homeAbility: 16, awayAbility: 10, new MatchInstructionsV1());
            var strongAway = TestMatchFactory.Build(seed, homeAbility: 10, awayAbility: 16, new MatchInstructionsV1());

            var first = MatchSimulator.Simulate(strongAtHome);
            var second = MatchSimulator.Simulate(strongAway);

            strongHomeWins += first.HomeGoals > first.AwayGoals ? 1 : 0;
            strongAwayWins += second.AwayGoals > second.HomeGoals ? 1 : 0;
        }

        strongHomeWins.Should().BeGreaterThan(240, "a much better side at home wins most of its matches");
        strongAwayWins.Should().BeGreaterThan(180, "and a much better side away still wins more than half");
    }

    private static Totals Measure()
    {
        long homeGoals = 0;
        long awayGoals = 0;
        long homeWins = 0;
        long draws = 0;
        long awayWins = 0;
        long shots = 0;
        long homePossession = 0;
        long fouls = 0;
        long yellows = 0;
        long reds = 0;
        long injuries = 0;
        long penalties = 0;
        long substitutions = 0;
        long highScoring = 0;
        var highest = 0;

        for (var index = 0; index < Matches; index++)
        {
            var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed: 1_000 + (ulong)index));

            homeGoals += result.HomeGoals;
            awayGoals += result.AwayGoals;
            shots += result.Home.Shots + result.Away.Shots;
            homePossession += result.Home.PossessionBasisPoints;
            fouls += result.Home.Fouls + result.Away.Fouls;
            yellows += result.Home.YellowCards + result.Away.YellowCards;
            reds += result.Home.RedCards + result.Away.RedCards;
            injuries += result.Home.Injuries + result.Away.Injuries;
            penalties += result.Home.PenaltiesAwarded + result.Away.PenaltiesAwarded;
            substitutions += result.Home.Substitutions + result.Away.Substitutions;

            homeWins += result.HomeGoals > result.AwayGoals ? 1 : 0;
            draws += result.HomeGoals == result.AwayGoals ? 1 : 0;
            awayWins += result.HomeGoals < result.AwayGoals ? 1 : 0;

            var total = result.HomeGoals + result.AwayGoals;
            highScoring += total >= 7 ? 1 : 0;
            highest = Math.Max(highest, total);
        }

        return new Totals
        {
            GoalsPerMatch = (double)(homeGoals + awayGoals) / Matches,
            HomeWinShare = (double)homeWins / Matches,
            DrawShare = (double)draws / Matches,
            AwayWinShare = (double)awayWins / Matches,
            ShotsPerMatch = (double)shots / Matches,
            HomePossessionShare = (double)homePossession / Matches / 10_000,
            FoulsPerMatch = (double)fouls / Matches,
            YellowsPerMatch = (double)yellows / Matches,
            RedsPerMatch = (double)reds / Matches,
            InjuriesPerMatch = (double)injuries / Matches,
            PenaltiesPerMatch = (double)penalties / Matches,
            SubstitutionsPerMatch = (double)substitutions / Matches,
            HighScoringShare = (double)highScoring / Matches,
            HighestTotalGoals = highest,
        };
    }

    private sealed record Totals
    {
        public required double GoalsPerMatch { get; init; }

        public required double HomeWinShare { get; init; }

        public required double DrawShare { get; init; }

        public required double AwayWinShare { get; init; }

        public required double ShotsPerMatch { get; init; }

        public required double HomePossessionShare { get; init; }

        public required double FoulsPerMatch { get; init; }

        public required double YellowsPerMatch { get; init; }

        public required double RedsPerMatch { get; init; }

        public required double InjuriesPerMatch { get; init; }

        public required double PenaltiesPerMatch { get; init; }

        public required double SubstitutionsPerMatch { get; init; }

        public required double HighScoringShare { get; init; }

        public required int HighestTotalGoals { get; init; }
    }
}

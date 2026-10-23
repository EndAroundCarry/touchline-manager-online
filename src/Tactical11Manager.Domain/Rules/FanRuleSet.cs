using Tactical11Manager.Domain.Fans;

namespace Tactical11Manager.Domain.Rules;

/// <summary>
/// The fan rules: how the fans size a match up before it is played, and how a result, a week, a season, a change of division or a new
/// manager moves their number and their mood (game rules §13.6, `FAN-*`). This file holds the expectation; the other half is in
/// <c>FanRuleSet.FanBase.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Kept beside <see cref="WorldRuleSet"/> and <see cref="StadiumRuleSet"/> because every value is a balancing value, but stamped by its own
/// <see cref="Version"/>: a fan rule changes how the crowd feels, not what a match costs or how it is played, so it does not move the
/// world rule set's version until the crowd and the gate read from it (Milestone 4).
/// </para>
/// <para>
/// Every quantity is an integer: strength in hundredths of an attribute point (<see cref="TeamStrength"/>), chances in basis points
/// (10,000 is certain), expected points and goals in hundredths. The one exception is the score the fans bet on, which weighs Poisson
/// chances in floating point because it only picks between a handful of scorelines and is stored the moment it is made.
/// </para>
/// </remarks>
public static partial class FanRuleSet
{
    /// <summary>The version of the fan rules, stored with every fan row so a number can be traced to the rules that made it.</summary>
    public const string Version = "fan-rules-v2";

    /// <summary>How far apart the rows of the strength curve are, in hundredths of an attribute point.</summary>
    public const int CurveStepCentis = 50;

    /// <summary>The widest strength gap the curve tells apart, in hundredths of a point; a wider gap reads as this one.</summary>
    public const int CurveLimitCentis = 600;

    /// <summary>
    /// The number of games at which form counts for as much as the squad (`FAN-3`). Form's weight is <c>games / (games + this)</c>: nothing
    /// before the first game, half after ten, three quarters after thirty.
    /// </summary>
    public const int FormHalfWeightGames = 10;

    /// <summary>
    /// What a point a game is worth in strength, as the points a side takes a game for each attribute point of strength, in hundredths
    /// (`FAN-3`). Measured on the tick engine around an even game and averaged over home and away (<c>tickfans pool</c>).
    /// </summary>
    public const int FormPointsPerAttributePointCentis = 22;

    /// <summary>Expected points from which the fans say they should hammer the opponent, in hundredths (`FAN-4`).</summary>
    public const int ShouldHammerThemFromCentis = 240;

    /// <summary>Expected points from which the fans expect a win, in hundredths (`FAN-4`).</summary>
    public const int ExpectAWinFromCentis = 200;

    /// <summary>Expected points from which the fans say the side should win, in hundredths (`FAN-4`).</summary>
    public const int ShouldWinFromCentis = 160;

    /// <summary>Expected points from which the fans call it a game that could go either way, in hundredths (`FAN-4`).</summary>
    public const int CouldGoEitherWayFromCentis = 120;

    /// <summary>Expected points from which the fans would settle for a point, in hundredths (`FAN-4`).</summary>
    public const int APointWouldDoFromCentis = 80;

    /// <summary>Expected points from which the fans call it a tough game, in hundredths; below it they hope to limit the damage (`FAN-4`).</summary>
    public const int ToughGameFromCentis = 50;

    /// <summary>The most goals a side is given in the score the fans bet on.</summary>
    private const int MostGoals = 7;

    private const int OneHundredPercent = 10_000;

    /// <summary>
    /// The home side's chance of winning by strength gap, in basis points, from -600 to +600 hundredths in steps of
    /// <see cref="CurveStepCentis"/> (25 rows). An ordered-logit curve fitted to 1,800 matches of the tick engine between the dev world's
    /// own squads, with the gaps spread evenly (<c>tickfans real</c>, <c>tickfans pool</c>, seeds 7200-7202, 2026-10-20).
    /// </summary>
    private static readonly int[] HomeWinBp = [980, 1140, 1324, 1531, 1764, 2025, 2313, 2629, 2971, 3337, 3725, 4130, 4547, 4970, 5394, 5812, 6219, 6609, 6979, 7324, 7644, 7936, 8200, 8437, 8649];

    /// <summary>The chance of a draw by strength gap, in basis points; the same rows as <see cref="HomeWinBp"/>.</summary>
    private static readonly int[] DrawBp = [1669, 1852, 2037, 2218, 2391, 2548, 2683, 2791, 2867, 2906, 2908, 2871, 2798, 2693, 2559, 2404, 2233, 2052, 1867, 1684, 1506, 1337, 1179, 1034, 902];

    /// <summary>The goals the home side is expected to score by strength gap, in hundredths; the same rows as <see cref="HomeWinBp"/>.</summary>
    private static readonly int[] HomeGoalsCentis = [88, 94, 99, 105, 112, 118, 126, 133, 141, 150, 159, 168, 179, 189, 201, 213, 226, 239, 254, 269, 286, 303, 321, 340, 361];

    /// <summary>The goals the away side is expected to score by strength gap, in hundredths; the same rows as <see cref="HomeWinBp"/>.</summary>
    private static readonly int[] AwayGoalsCentis = [298, 277, 258, 240, 224, 208, 194, 181, 168, 157, 146, 136, 126, 118, 109, 102, 95, 88, 82, 77, 71, 66, 62, 58, 54];

    /// <summary>The strength gap the fans see between a home and an away side, in hundredths of an attribute point (`FAN-3`).</summary>
    /// <param name="homeStrength">The home side's <see cref="TeamStrength"/>.</param>
    /// <param name="homeForm">The home side's season so far.</param>
    /// <param name="awayStrength">The away side's <see cref="TeamStrength"/>.</param>
    /// <param name="awayForm">The away side's season so far.</param>
    /// <returns>
    /// The squad gap, blended with the form gap by the form's weight. The weight is read at the games the side with fewer games has played,
    /// so a side that has played less never lets form speak louder than it has earned. A form gap in points a game is turned into strength
    /// by <see cref="FormPointsPerAttributePointCentis"/>.
    /// </returns>
    public static int GapCentis(int homeStrength, FanForm homeForm, int awayStrength, FanForm awayForm)
    {
        var games = Math.Max(0, Math.Min(homeForm.Games, awayForm.Games));
        var formWeightBp = (int)((long)games * OneHundredPercent / (games + FormHalfWeightGames));
        var squadGap = homeStrength - awayStrength;
        var formGap = (homeForm.PointsPerGameCentis - awayForm.PointsPerGameCentis) * 100 / FormPointsPerAttributePointCentis;

        return (int)((((long)(OneHundredPercent - formWeightBp) * squadGap) + ((long)formWeightBp * formGap)) / OneHundredPercent);
    }

    /// <summary>What the fans of one side expect from a match (`FAN-4`).</summary>
    /// <param name="strength">The side's <see cref="TeamStrength"/>.</param>
    /// <param name="form">The side's season so far.</param>
    /// <param name="opponentStrength">The opponent's <see cref="TeamStrength"/>.</param>
    /// <param name="opponentForm">The opponent's season so far.</param>
    /// <param name="atHome">Whether the side plays at home.</param>
    /// <returns>The expectation, from the side's own point of view.</returns>
    public static FanExpectation ExpectationFor(int strength, FanForm form, int opponentStrength, FanForm opponentForm, bool atHome) =>
        ExpectationForGap(
            atHome ? GapCentis(strength, form, opponentStrength, opponentForm) : GapCentis(opponentStrength, opponentForm, strength, form),
            atHome);

    /// <summary>What the fans of one side expect, given the strength gap between the home and the away side.</summary>
    /// <param name="homeGapCentis">The home side's strength minus the away side's, in hundredths of an attribute point.</param>
    /// <param name="atHome">Whether the fans' side is the home side.</param>
    /// <returns>The expectation, from the point of view of the home side when <paramref name="atHome"/> is true and of the away side otherwise.</returns>
    public static FanExpectation ExpectationForGap(int homeGapCentis, bool atHome)
    {
        var (homeWinBp, drawBp, homeGoalsCentis, awayGoalsCentis) = CurveAt(homeGapCentis);
        var lossBp = OneHundredPercent - homeWinBp - drawBp;
        var winBp = atHome ? homeWinBp : lossBp;
        var againstBp = atHome ? lossBp : homeWinBp;
        var points = ((3 * winBp) + drawBp + 50) / 100;
        var label = LabelFor(points);
        var (goalsFor, goalsAgainst) = PredictedScore(
            atHome ? homeGoalsCentis : awayGoalsCentis,
            atHome ? awayGoalsCentis : homeGoalsCentis,
            label);

        return new FanExpectation(points, winBp, drawBp, againstBp, label, goalsFor, goalsAgainst);
    }

    /// <summary>The goals each side is expected to score, by the strength gap between the home and the away side (`FAN-4`).</summary>
    /// <param name="homeGapCentis">The home side's strength minus the away side's, in hundredths of an attribute point.</param>
    /// <returns>The home side's and the away side's expected goals, in hundredths.</returns>
    public static (int HomeGoalsCentis, int AwayGoalsCentis) ExpectedGoalsForGap(int homeGapCentis)
    {
        var (_, _, homeGoalsCentis, awayGoalsCentis) = CurveAt(homeGapCentis);

        return (homeGoalsCentis, awayGoalsCentis);
    }

    /// <summary>The label the fans give an expected number of points (`FAN-4`).</summary>
    /// <param name="expectedPointsCentis">The expected points in hundredths.</param>
    public static FanExpectationLabel LabelFor(int expectedPointsCentis) => expectedPointsCentis switch
    {
        >= ShouldHammerThemFromCentis => FanExpectationLabel.ShouldHammerThem,
        >= ExpectAWinFromCentis => FanExpectationLabel.ExpectAWin,
        >= ShouldWinFromCentis => FanExpectationLabel.ShouldWin,
        >= CouldGoEitherWayFromCentis => FanExpectationLabel.CouldGoEitherWay,
        >= APointWouldDoFromCentis => FanExpectationLabel.APointWouldDo,
        >= ToughGameFromCentis => FanExpectationLabel.ToughGame,
        _ => FanExpectationLabel.DamageLimitation,
    };

    /// <summary>
    /// The score the fans would bet on: the most likely one when each side's goals follow a Poisson law around its expected goals, among
    /// the scores the label allows (`FAN-4`).
    /// </summary>
    /// <remarks>
    /// A label that expects a win only allows a win, a label that expects to lose at least a point only allows a draw or a defeat, and
    /// "Damage limitation" only a defeat, so the score never contradicts the words beside it. Between them anything goes. Of two equally
    /// likely scores the one with fewer goals wins, then the one with more for the side.
    /// </remarks>
    /// <param name="goalsForCentis">The goals the side is expected to score, in hundredths.</param>
    /// <param name="goalsAgainstCentis">The goals the side is expected to concede, in hundredths.</param>
    /// <param name="label">The label of the side's expectation.</param>
    /// <returns>The goals for and against.</returns>
    public static (int For, int Against) PredictedScore(int goalsForCentis, int goalsAgainstCentis, FanExpectationLabel label)
    {
        var forChances = PoissonChances(goalsForCentis / 100.0);
        var againstChances = PoissonChances(goalsAgainstCentis / 100.0);
        var best = (For: 0, Against: 0);
        var bestChance = -1.0;

        for (var scored = 0; scored <= MostGoals; scored++)
        {
            for (var conceded = 0; conceded <= MostGoals; conceded++)
            {
                if (!Allows(label, scored, conceded))
                {
                    continue;
                }

                var chance = forChances[scored] * againstChances[conceded];
                var better = chance > bestChance + 1e-12
                    || (Math.Abs(chance - bestChance) <= 1e-12 && IsPreferredTie(scored, conceded, best));

                if (better)
                {
                    best = (scored, conceded);
                    bestChance = chance;
                }
            }
        }

        return best;
    }

    private static bool Allows(FanExpectationLabel label, int scored, int conceded) => label switch
    {
        >= FanExpectationLabel.ShouldWin => scored > conceded,
        FanExpectationLabel.DamageLimitation => scored < conceded,
        FanExpectationLabel.ToughGame or FanExpectationLabel.APointWouldDo => scored <= conceded,
        _ => true,
    };

    private static bool IsPreferredTie(int scored, int conceded, (int For, int Against) held) =>
        scored + conceded < held.For + held.Against
        || (scored + conceded == held.For + held.Against && scored > held.For);

    private static double[] PoissonChances(double mean)
    {
        var chances = new double[MostGoals + 1];

        chances[0] = Math.Exp(-mean);

        for (var goals = 1; goals <= MostGoals; goals++)
        {
            chances[goals] = chances[goals - 1] * mean / goals;
        }

        return chances;
    }

    /// <summary>Reads the curve at a gap, interpolating between its rows in whole numbers.</summary>
    private static (int HomeWinBp, int DrawBp, int HomeGoalsCentis, int AwayGoalsCentis) CurveAt(int gapCentis)
    {
        var clamped = Math.Clamp(gapCentis, -CurveLimitCentis, CurveLimitCentis);
        var offset = clamped + CurveLimitCentis;
        var row = Math.Min(offset / CurveStepCentis, HomeWinBp.Length - 2);
        var across = offset - (row * CurveStepCentis);

        int Between(int[] column)
        {
            var span = (column[row + 1] - column[row]) * across;

            // Half a step either way, so the rounding is symmetric whichever way the column runs.
            return column[row] + ((span + (Math.Sign(span) * (CurveStepCentis / 2))) / CurveStepCentis);
        }

        return (Between(HomeWinBp), Between(DrawBp), Between(HomeGoalsCentis), Between(AwayGoalsCentis));
    }
}

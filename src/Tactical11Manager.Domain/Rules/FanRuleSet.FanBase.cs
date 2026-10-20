using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Rules;

/// <summary>The fan count and the fan mood: the opening numbers, what a result, a week, a season, a tier change and a takeover do (`FAN-1`, `FAN-2`, `FAN-5`..`FAN-8`).</summary>
public static partial class FanRuleSet
{
    // ---- the fan count, by tier -----------------------------------------------------------------------------------------------------

    /// <summary>The fans a tier-1 club starts with (`FAN-1`). A lower tier starts with <see cref="TierRetainedNumerator"/> / <see cref="TierRetainedDenominator"/> of the tier above.</summary>
    public const int OpeningFansTier1 = 10_000;

    /// <summary>The share of the fans who want to come to a home match, in basis points (`FAN-1`, `STAD-5`): 3,000 of 10,000 fans is 3,000 people at tier 1.</summary>
    public const int WantToComeShareBp = 3_000;

    /// <summary>The floor as a share of a tier's opening fans, in basis points (`FAN-1`): the loyal core that never leaves.</summary>
    public const int FloorShareOfOpeningBp = 4_000;

    /// <summary>The most people who want to come to a tier-1 club's home match, the soft cap, a little above the biggest stadium (`FAN-1`).</summary>
    public const int SoftCapWantToComeTier1 = 55_000;

    /// <summary>The numerator of what a tier keeps of the tier above it: three quarters, the same fall as the crowd's (`STAD-5`).</summary>
    public const int TierRetainedNumerator = 3;

    /// <summary>The denominator of what a tier keeps of the tier above it.</summary>
    public const int TierRetainedDenominator = 4;

    // ---- how a result moves the count -----------------------------------------------------------------------------------------------

    /// <summary>People who want to come who join after a win the fans did not expect, expected points under <see cref="UnderdogBelowCentis"/> (`FAN-5`).</summary>
    public const int JoinAfterUnderdogWin = 600;

    /// <summary>People who join after winning an even game (`FAN-5`).</summary>
    public const int JoinAfterEvenWin = 450;

    /// <summary>People who join after winning as the favourite, expected points from <see cref="FavouriteFromCentis"/> (`FAN-5`).</summary>
    public const int JoinAfterFavouriteWin = 325;

    /// <summary>People who join after a draw the fans did not expect, which is worth less than a win as the favourite (`FAN-5`).</summary>
    public const int JoinAfterUnderdogDraw = 100;

    /// <summary>People who leave after losing as the favourite (`FAN-5`).</summary>
    public const int LeaveAfterFavouriteLoss = 300;

    /// <summary>People who leave after losing an even game; losing as the underdog costs nothing (`FAN-5`).</summary>
    public const int LeaveAfterEvenLoss = 250;

    /// <summary>The least that is added to a join or a leave, in people who want to come; the amount is never below 0 once added (`FAN-5`).</summary>
    public const int MatchRandomLow = -15;

    /// <summary>The most that is added to a join or a leave, in people who want to come (`FAN-5`).</summary>
    public const int MatchRandomHigh = 50;

    /// <summary>Expected points below which the fans see themselves as the underdog, in hundredths (`FAN-5`).</summary>
    public const int UnderdogBelowCentis = 100;

    /// <summary>Expected points from which the fans see themselves as the favourite, in hundredths (`FAN-5`).</summary>
    public const int FavouriteFromCentis = 160;

    /// <summary>How much of the soft cap the gains fade out over, in basis points: the last quarter below the cap (`FAN-5`).</summary>
    public const int GainFadeShareBp = 2_500;

    /// <summary>The extra fans a club's very first season brings for each step above tier 3, as a share of its fans, in basis points, spread over the season (`FAN-6`).</summary>
    public const int FirstSeasonBoostPerStepBp = 500;

    /// <summary>The tier below which a club gets no first-season boost: tier 1 gets two steps, tier 2 one, tier 3 none (`FAN-6`).</summary>
    public const int FirstSeasonBoostTiers = 3;

    /// <summary>The matches over which the first-season boost is paid out: a 34-match league season (`FAN-6`).</summary>
    public const int FirstSeasonMatches = 34;

    /// <summary>The fans a club gains the moment it is promoted, per tier it climbs, in basis points (`FAN-7`).</summary>
    public const int PromotionBp = 1_000;

    /// <summary>The fans a club loses the moment it is relegated, per tier it falls, in basis points (`FAN-7`).</summary>
    public const int RelegationBp = 1_000;

    /// <summary>
    /// The share of the fans above the soft cap who drift away each season, in basis points (`FAN-7`). A relegated club keeps most of a
    /// crowd its new division cannot hold, and loses it over a few seasons rather than at once.
    /// </summary>
    public const int OverCapDriftBp = 2_500;

    // ---- the mood -------------------------------------------------------------------------------------------------------------------

    /// <summary>The top of the mood scale, in basis points: 10,000 is euphoric (`FAN-2`).</summary>
    public const int MoodMaxBp = 10_000;

    /// <summary>The middle of the scale, calm, in basis points: where a new club starts and where the fans drift back to (`FAN-2`).</summary>
    public const int CalmMoodBp = 5_000;

    /// <summary>How hard a surprise of one point moves the mood, in basis points (`FAN-2`). The move is this times the surprise to the power ¾.</summary>
    public const int SurpriseSizeBp = 300;

    /// <summary>How much more a defeat that was not expected weighs than a win that was not, in basis points: 15,000 is one and a half times (`FAN-2`).</summary>
    public const int LossWeightBp = 15_000;

    /// <summary>The most the goal margin adds to or takes from the mood after a match, in basis points (`FAN-2`).</summary>
    public const int MarginMaxBp = 100;

    /// <summary>The goal margin at which the margin term is at its largest (`FAN-2`).</summary>
    public const int MarginFullGoals = 4;

    /// <summary>How much of the gap to calm the mood closes after each match, in basis points (`FAN-2`).</summary>
    public const int PullToCalmBp = 200;

    /// <summary>
    /// A constant added to the mood after every match, in basis points, so that a club which exactly meets what its fans expect stays at
    /// calm instead of slowly souring (`FAN-2`): the defeats that weigh more than the wins would otherwise pull it down.
    /// Measured on the engine's own curve over a league (`FanRuleSetFanBaseTests`).
    /// </summary>
    public const int NeutralOffsetBp = 88;

    /// <summary>What a place against the target is worth each week, in basis points of mood (`FAN-8`).</summary>
    public const int WeeklyPlaceBp = 40;

    /// <summary>What a place gained or lost since last week is worth, in basis points of mood (`FAN-8`).</summary>
    public const int WeeklyTrendBp = 100;

    /// <summary>The most places against the target that count in a review, either way (`FAN-8`).</summary>
    public const int GapLimitPlaces = 6;

    /// <summary>What each place against the target is worth in the season's last verdict, in basis points of mood (`FAN-8`).</summary>
    public const int VerdictPlaceBp = 150;

    /// <summary>How much of its distance from calm the mood keeps over a season's end, in basis points: half, as in Hattrick (`FAN-8`).</summary>
    public const int SeasonEndKeepBp = 5_000;

    private const int Hundredths = 100;

    /// <summary>The fans a club in the given tier starts with (`FAN-1`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int StartFansFor(int tier) => ForTier(OpeningFansTier1, tier);

    /// <summary>The fewest fans a club in the given tier can have (`FAN-1`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int FloorFansFor(int tier) => Math.Max(1, (int)DivideRounded((long)StartFansFor(tier) * FloorShareOfOpeningBp, OneHundredPercent));

    /// <summary>The fans above which a club in the given tier stops winning new ones (`FAN-1`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int SoftCapFansFor(int tier) => ForTier(PeopleToFans(SoftCapWantToComeTier1), tier);

    /// <summary>The people who want to come to a home match out of a number of fans, before the ground limits them (`STAD-5`).</summary>
    /// <param name="fans">The fans.</param>
    public static int WantToComeFor(int fans) => (int)DivideRounded((long)fans * WantToComeShareBp, OneHundredPercent);

    /// <summary>The state of a club's fans at the start of its life, and the state a new manager gets (`FAN-1`, `FAN-9`).</summary>
    /// <param name="tier">The tier number the club plays in, starting at 1.</param>
    /// <returns>The tier's opening fans, calm, and no review behind it.</returns>
    public static FanBaseState OpeningFor(int tier) => new(StartFansFor(tier), CalmMoodBp);

    /// <summary>
    /// What a human manager's fans are when the manager takes the club over (`FAN-9`): the tier's opening numbers, however the club was doing.
    /// </summary>
    /// <param name="tier">The tier number the club plays in, starting at 1.</param>
    public static FanBaseState OnTakeover(int tier) => OpeningFor(tier);

    // ---- a match --------------------------------------------------------------------------------------------------------------------

    /// <summary>The number added to a join or a leave in a match, in people who want to come: the same for the same match and club every time (`FAN-5`).</summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="clubId">The club whose fans these are.</param>
    /// <returns>A whole number from <see cref="MatchRandomLow"/> to <see cref="MatchRandomHigh"/>.</returns>
    public static int MatchRandomFor(Guid fixtureId, Guid clubId)
    {
        var random = new Pcg32(DeterministicDigest.SeedOf(Version, fixtureId.ToString("D"), clubId.ToString("D")));

        return MatchRandomLow + random.NextInt(MatchRandomHigh - MatchRandomLow + 1);
    }

    /// <summary>How a result moves the mood on its own, before the pull to calm and the constant offset, in basis points (`FAN-2`).</summary>
    /// <remarks>
    /// The surprise is the points won minus the points the fans expected. The move is <see cref="SurpriseSizeBp"/> times the size of the
    /// surprise to the power ¾ (so a big surprise counts for less than double), one and a half times as much for a bad surprise, plus up
    /// to <see cref="MarginMaxBp"/> for the goal margin. The power is a square root of a square root so the result is exactly the same on
    /// every machine.
    /// </remarks>
    /// <param name="expectedPointsCentis">The points the fans expected, in hundredths.</param>
    /// <param name="goalsFor">The goals the club scored.</param>
    /// <param name="goalsAgainst">The goals the club conceded.</param>
    public static int MatchMoodShiftBp(int expectedPointsCentis, int goalsFor, int goalsAgainst)
    {
        var surprise = PointsFor(goalsFor, goalsAgainst) * Hundredths - expectedPointsCentis;
        var size = Math.Abs(surprise) / (double)Hundredths;
        var move = SurpriseSizeBp * Math.Sqrt(size) * Math.Sqrt(Math.Sqrt(size));

        if (surprise < 0)
        {
            move = -move * LossWeightBp / OneHundredPercent;
        }

        var margin = Math.Min(Math.Abs(goalsFor - goalsAgainst), MarginFullGoals) * MarginMaxBp / MarginFullGoals;
        var shift = (int)Math.Round(move, MidpointRounding.AwayFromZero) + (Math.Sign(goalsFor - goalsAgainst) * margin);

        return shift;
    }

    /// <summary>What one league match does to a club's fans: the mood and the count (`FAN-2`, `FAN-5`, `FAN-6`).</summary>
    /// <param name="state">The fans before the match.</param>
    /// <param name="tier">The tier the club plays in, starting at 1.</param>
    /// <param name="expectedPointsCentis">The points the fans expected before kick-off, in hundredths (frozen at team-sheet lock).</param>
    /// <param name="goalsFor">The goals the club scored.</param>
    /// <param name="goalsAgainst">The goals the club conceded.</param>
    /// <param name="fixtureId">The fixture, which with the club sets the random amount.</param>
    /// <param name="clubId">The club whose fans these are.</param>
    /// <param name="firstSeason">Whether this is the club's very first season, which earns the boost.</param>
    /// <returns>
    /// The fans after the match. The mood moves by the surprise, the pull to calm and the constant offset. The count rises after a win
    /// (and an underdog's draw) by an amount that depends on what the fans expected, fading out below the soft cap and stopping at it;
    /// it falls after a defeat they did not take for granted, never below the floor.
    /// </returns>
    public static FanBaseState AfterMatch(
        FanBaseState state,
        int tier,
        int expectedPointsCentis,
        int goalsFor,
        int goalsAgainst,
        Guid fixtureId,
        Guid clubId,
        bool firstSeason)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        var mood = state.MoodBp
            + MatchMoodShiftBp(expectedPointsCentis, goalsFor, goalsAgainst)
            + (int)(((long)(CalmMoodBp - state.MoodBp) * PullToCalmBp) / OneHundredPercent)
            + NeutralOffsetBp;

        var points = PointsFor(goalsFor, goalsAgainst);
        var random = MatchRandomFor(fixtureId, clubId);
        var joined = points switch
        {
            3 when expectedPointsCentis < UnderdogBelowCentis => JoinAfterUnderdogWin,
            3 when expectedPointsCentis < FavouriteFromCentis => JoinAfterEvenWin,
            3 => JoinAfterFavouriteWin,
            1 when expectedPointsCentis < UnderdogBelowCentis => JoinAfterUnderdogDraw,
            _ => 0,
        };
        var left = points != 0 ? 0 : expectedPointsCentis >= FavouriteFromCentis ? LeaveAfterFavouriteLoss : expectedPointsCentis >= UnderdogBelowCentis ? LeaveAfterEvenLoss : 0;

        long change = 0;

        if (joined > 0)
        {
            var gain = ForTier(PeopleToFans(Math.Max(0, joined + random)), tier);

            change += DivideRounded((long)gain * GainFadeFactorBp(state.Fans, tier), OneHundredPercent);
        }

        if (left > 0)
        {
            change -= ForTier(PeopleToFans(Math.Max(0, left + random)), tier);
        }

        if (firstSeason)
        {
            var boostBp = (long)FirstSeasonBoostPerStepBp * Math.Max(0, FirstSeasonBoostTiers - tier);

            change += DivideRounded(state.Fans * boostBp, (long)FirstSeasonMatches * OneHundredPercent);
        }

        var fans = (int)Math.Max(FloorFansFor(tier), state.Fans + change);

        return state with { Fans = fans, MoodBp = Math.Clamp(mood, 0, MoodMaxBp) };
    }

    // ---- a week ---------------------------------------------------------------------------------------------------------------------

    /// <summary>What a weekly review does to the mood: how close the club is to the fans' target, and whether it is closing in (`FAN-8`).</summary>
    /// <param name="state">The fans before the review.</param>
    /// <param name="gap">The club's projected place minus the target place: positive is worse than the fans hoped.</param>
    /// <param name="gamesPlayed">The league games the club has played this season.</param>
    /// <param name="seasonGames">The league games in the season.</param>
    /// <returns>
    /// The fans after the review. The move is -<see cref="WeeklyPlaceBp"/> for each place of the gap (at most
    /// <see cref="GapLimitPlaces"/> either way) plus <see cref="WeeklyTrendBp"/> for each place gained since last week, counted for
    /// <c>gamesPlayed / seasonGames</c> of its size so an early review hardly matters. The count does not change.
    /// </returns>
    public static FanBaseState AfterWeek(FanBaseState state, int gap, int gamesPlayed, int seasonGames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonGames, 1);

        var played = Math.Clamp(gamesPlayed, 0, seasonGames);
        var move = -WeeklyPlaceBp * Math.Clamp(gap, -GapLimitPlaces, GapLimitPlaces)
            + (state.LastWeekGap is { } last ? WeeklyTrendBp * (last - gap) : 0);
        var scaled = (int)DivideRounded((long)move * played, seasonGames);

        return state with { MoodBp = Math.Clamp(state.MoodBp + scaled, 0, MoodMaxBp), LastWeekGap = gap };
    }

    // ---- a season -------------------------------------------------------------------------------------------------------------------

    /// <summary>What the end of a season does to a club's fans: the last verdict, the move halfway back to calm, and the slow fall of a crowd the division cannot hold (`FAN-7`, `FAN-8`).</summary>
    /// <param name="state">The fans after the last match of the season.</param>
    /// <param name="tier">The tier the club played the season in, starting at 1.</param>
    /// <param name="finalGap">The final place minus the target place, or null when the club had no target.</param>
    /// <returns>
    /// The fans for the new season. The verdict is -<see cref="VerdictPlaceBp"/> for each place of the gap (at most
    /// <see cref="GapLimitPlaces"/>), and then the mood keeps <see cref="SeasonEndKeepBp"/> of its distance from calm. Fans above the
    /// tier's soft cap lose <see cref="OverCapDriftBp"/> of the excess. The week-to-week gap is cleared for the new target.
    /// </returns>
    public static FanBaseState AfterSeason(FanBaseState state, int tier, int? finalGap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        var verdict = finalGap is { } gap ? -VerdictPlaceBp * Math.Clamp(gap, -GapLimitPlaces, GapLimitPlaces) : 0;
        var judged = Math.Clamp(state.MoodBp + verdict, 0, MoodMaxBp);
        var mood = CalmMoodBp + (int)DivideRounded((long)(judged - CalmMoodBp) * SeasonEndKeepBp, OneHundredPercent);
        var cap = SoftCapFansFor(tier);
        var fans = state.Fans > cap ? state.Fans - (int)DivideRounded((long)(state.Fans - cap) * OverCapDriftBp, OneHundredPercent) : state.Fans;

        return new FanBaseState(Math.Max(FloorFansFor(tier), fans), Math.Clamp(mood, 0, MoodMaxBp));
    }

    /// <summary>What a change of division does to a club's fans: 10% more for each tier it climbs, 10% fewer for each it falls (`FAN-7`).</summary>
    /// <param name="state">The fans at the end of the season.</param>
    /// <param name="fromTier">The tier the club played in.</param>
    /// <param name="toTier">The tier it plays in next season.</param>
    /// <returns>The fans, never below the new tier's floor. A club above its new soft cap is left there; the cap drifts it down over the seasons (<see cref="AfterSeason"/>).</returns>
    public static FanBaseState OnTierChange(FanBaseState state, int fromTier, int toTier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fromTier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(toTier, 1);

        long fans = state.Fans;

        for (var step = 0; step < fromTier - toTier; step++)
        {
            fans = DivideRounded(fans * (OneHundredPercent + PromotionBp), OneHundredPercent);
        }

        for (var step = 0; step < toTier - fromTier; step++)
        {
            fans = DivideRounded(fans * (OneHundredPercent - RelegationBp), OneHundredPercent);
        }

        return state with { Fans = (int)Math.Max(FloorFansFor(toTier), fans) };
    }

    private static int PointsFor(int goalsFor, int goalsAgainst) => goalsFor > goalsAgainst ? 3 : goalsFor == goalsAgainst ? 1 : 0;

    private static int PeopleToFans(int people) => (int)DivideRounded((long)people * OneHundredPercent, WantToComeShareBp);

    /// <summary>How much of a gain a club keeps, in basis points: all of it far below the soft cap, none at or above it.</summary>
    private static int GainFadeFactorBp(int fans, int tier)
    {
        var cap = SoftCapFansFor(tier);
        var room = (long)(cap - fans) * OneHundredPercent * OneHundredPercent;

        return (int)Math.Clamp(room / ((long)cap * GainFadeShareBp), 0, OneHundredPercent);
    }

    /// <summary>Scales a tier-1 number to a tier, three quarters for each tier down.</summary>
    private static int ForTier(int tier1Value, int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        long value = tier1Value;

        for (var step = 1; step < tier && value > 1; step++)
        {
            value = DivideRounded(value * TierRetainedNumerator, TierRetainedDenominator);
        }

        return (int)value;
    }

    /// <summary>Divides rounding half away from zero.</summary>
    private static long DivideRounded(long numerator, long denominator)
    {
        var half = denominator / 2;

        return (numerator >= 0 ? numerator + half : numerator - half) / denominator;
    }
}

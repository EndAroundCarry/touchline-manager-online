using Tactical11Manager.Domain.Fans;

namespace Tactical11Manager.Domain.Rules;

/// <summary>A bonus on a building's takings, and the reason it is what it is.</summary>
/// <param name="Bp">The bonus in basis points of the base takings; 0 for none.</param>
/// <param name="Reason">A stable code for why: a result such as <c>underdog_win</c>, or a fan mood word's code.</param>
public readonly record struct StructureBonus(int Bp, string Reason);

/// <summary>
/// The rules of the snack bar and the merchandise shop: what each level earns and costs, and how the result
/// and the fans' mood lift the takings (game rules §13.7, `STRC-*`).
/// </summary>
/// <remarks>
/// <para>
/// Every number a finance rework would want to retune is in this one file, as a table indexed by level, so
/// the rework changes values and not code. All of them are provisional until that rework (`STRC-9`).
/// </para>
/// <para>
/// Money is an integer count of minor units (`FIN-2`), so the figures below are hundredths of the currency a
/// manager reads: a snack bar spend of 100 is 1.00 per spectator. What a spectator or a fan spends is the
/// same in every tier; a lower division earns less only because its crowd and its fans are smaller. Upkeep
/// and the cost of an upgrade are baselines like any other, so they are divided by
/// <see cref="WorldRuleSet.TierScalingFactor"/> for a club below the first tier (`STRC-9`).
/// </para>
/// </remarks>
public static class StructureRuleSet
{
    /// <summary>The highest level of either building (`STRC-1`).</summary>
    public const int MaxLevel = 10;

    /// <summary>The level every building opens at, free (`STRC-1`).</summary>
    public const int OpeningLevel = 1;

    /// <summary>The most the snack bar's takings can be lifted by a result, in basis points: 30% (`STRC-5`).</summary>
    public const int MaxResultBonusBp = 3_000;

    /// <summary>The bonus for winning when the fans expected to lose, in basis points (`STRC-5`).</summary>
    public const int UnderdogWinBonusBp = 3_000;

    /// <summary>The bonus for winning an even game, in basis points (`STRC-5`).</summary>
    public const int EvenWinBonusBp = 1_500;

    /// <summary>The bonus for winning as the favourite, in basis points (`STRC-5`).</summary>
    public const int FavouriteWinBonusBp = 500;

    /// <summary>The bonus for drawing when the fans expected to lose, in basis points (`STRC-5`).</summary>
    public const int UnderdogDrawBonusBp = 1_000;

    /// <summary>The reason code when the result earns no bonus.</summary>
    public const string NoBonusReason = "none";

    /// <summary>The reason code for a win by the underdog.</summary>
    public const string UnderdogWinReason = "underdog_win";

    /// <summary>The reason code for a win in an even game.</summary>
    public const string EvenWinReason = "even_win";

    /// <summary>The reason code for a win by the favourite.</summary>
    public const string FavouriteWinReason = "favourite_win";

    /// <summary>The reason code for a draw by the underdog.</summary>
    public const string UnderdogDrawReason = "underdog_draw";

    private const int OneHundredPercent = 10_000;

    // Index 0 is level 1. A table rather than a formula, so a retune can bend any one step.
    private static readonly long[] SnackSpendTable = [100, 125, 150, 175, 200, 225, 250, 275, 300, 325];

    private static readonly long[] MerchSpendTable = [30, 40, 50, 60, 70, 80, 90, 100, 110, 120];

    private static readonly long[] UpkeepTier1 = [30_000, 45_000, 60_000, 80_000, 100_000, 125_000, 150_000, 180_000, 215_000, 250_000];

    // Index 0 is level 1, which opens free; a cost belongs to the level it buys.
    private static readonly long[] UpgradeCostTier1 = [0, 200_000, 230_000, 250_000, 290_000, 300_000, 340_000, 350_000, 360_000, 380_000];

    private static readonly int[] BuildDays = [0, 1, 1, 2, 2, 3, 4, 5, 6, 7];

    /// <summary>What the snack bar earns per spectator at a level, in minor units, in every tier (`STRC-4`, `STRC-9`).</summary>
    /// <param name="level">The level, 1–10.</param>
    public static long SnackSpendMinor(int level) => SnackSpendTable[IndexOf(level)];

    /// <summary>What the merchandise shop earns per fan per week at a level, in minor units, in every tier (`STRC-6`, `STRC-9`).</summary>
    /// <param name="level">The level, 1–10.</param>
    public static long MerchSpendMinor(int level) => MerchSpendTable[IndexOf(level)];

    /// <summary>What one building costs to keep each week at a level, at a tier-1 club, in minor units (`STRC-8`).</summary>
    /// <param name="level">The level, 1–10.</param>
    public static long UpkeepMinorTier1(int level) => UpkeepTier1[IndexOf(level)];

    /// <summary>What an upgrade costs at a tier-1 club, in minor units, by the level it reaches (`STRC-3`).</summary>
    /// <param name="targetLevel">The level the work reaches, 2–10.</param>
    /// <remarks>
    /// Set so an upgrade helps a club grow and does not hold it back: at a tier-1 club with its opening crowd
    /// and fans, the first upgrade of either building pays for itself in about two weeks and the last in about
    /// five, counting the upkeep each step adds. A bigger club pays back faster still.
    /// </remarks>
    public static long UpgradeCostMinorTier1(int targetLevel) => UpgradeCostTier1[UpgradeIndexOf(targetLevel)];

    /// <summary>What one building costs to keep each week at a level for a club in the given tier, in minor units (`STRC-8`, `STRC-9`).</summary>
    /// <param name="level">The level, 1–10.</param>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long UpkeepMinorFor(int level, int tier) =>
        Math.Max(1, UpkeepMinorTier1(level) / WorldRuleSet.TierScalingFactor(tier));

    /// <summary>What an upgrade costs a club in the given tier, in minor units (`STRC-3`, `STRC-9`).</summary>
    /// <param name="targetLevel">The level the work reaches, 2–10.</param>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long UpgradeCostMinorFor(int targetLevel, int tier) =>
        Math.Max(1, UpgradeCostMinorTier1(targetLevel) / WorldRuleSet.TierScalingFactor(tier));

    /// <summary>How many days an upgrade takes, by the level it reaches (`STRC-3`).</summary>
    /// <param name="targetLevel">The level the work reaches, 2–10.</param>
    public static int BuildDaysFor(int targetLevel) => BuildDays[UpgradeIndexOf(targetLevel)];

    /// <summary>The snack bar's bonus for a home match's result (`STRC-5`).</summary>
    /// <param name="goalsFor">The home side's goals.</param>
    /// <param name="goalsAgainst">The visitors' goals.</param>
    /// <param name="expectedPointsCentis">The home side's frozen expected points, in hundredths, the same figure the fans judge the result by.</param>
    /// <returns>
    /// A win as the underdog (expected points under <see cref="FanRuleSet.UnderdogBelowCentis"/>) earns 30%, a
    /// win in an even game 15%, a win as the favourite (from <see cref="FanRuleSet.FavouriteFromCentis"/>) 5%,
    /// and a draw as the underdog 10%. Any other result earns nothing. The cap is
    /// <see cref="MaxResultBonusBp"/>.
    /// </returns>
    public static StructureBonus ResultBonusFor(int goalsFor, int goalsAgainst, int expectedPointsCentis)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(goalsFor);
        ArgumentOutOfRangeException.ThrowIfNegative(goalsAgainst);

        var underdog = expectedPointsCentis < FanRuleSet.UnderdogBelowCentis;
        var favourite = expectedPointsCentis >= FanRuleSet.FavouriteFromCentis;

        if (goalsFor > goalsAgainst)
        {
            return underdog ? new StructureBonus(UnderdogWinBonusBp, UnderdogWinReason)
                : favourite ? new StructureBonus(FavouriteWinBonusBp, FavouriteWinReason)
                : new StructureBonus(EvenWinBonusBp, EvenWinReason);
        }

        return goalsFor == goalsAgainst && underdog
            ? new StructureBonus(UnderdogDrawBonusBp, UnderdogDrawReason)
            : new StructureBonus(0, NoBonusReason);
    }

    /// <summary>The merchandise shop's bonus for the fans' mood (`STRC-7`).</summary>
    /// <param name="word">The fans' mood word.</param>
    /// <returns>The bonus, with the mood word's code as the reason.</returns>
    public static StructureBonus MoodBonusFor(FanMoodWord word) => new(MoodBonusBpFor(word), FanMoods.Code(word));

    /// <summary>The merchandise shop's bonus for a mood word, in basis points (`STRC-7`).</summary>
    /// <param name="word">The fans' mood word.</param>
    public static int MoodBonusBpFor(FanMoodWord word) => word switch
    {
        FanMoodWord.Furious => 0,
        FanMoodWord.Angry => 0,
        FanMoodWord.Unhappy => 200,
        FanMoodWord.Calm => 500,
        FanMoodWord.Content => 1_000,
        FanMoodWord.Delighted => 1_800,
        FanMoodWord.Euphoric => 2_500,
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "Unknown fan mood word."),
    };

    /// <summary>The snack bar's takings for a home match before any bonus, in minor units (`STRC-4`).</summary>
    /// <param name="level">The snack bar's level.</param>
    /// <param name="spectators">The crowd, from <see cref="StadiumRuleSet.AttendanceFor"/>.</param>
    public static long SnackBaseMinorFor(int level, int spectators)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spectators);

        return spectators * SnackSpendMinor(level);
    }

    /// <summary>The merchandise shop's takings for a week before any bonus, in minor units (`STRC-6`).</summary>
    /// <param name="level">The shop's level.</param>
    /// <param name="fans">The club's fans.</param>
    public static long MerchBaseMinorFor(int level, int fans)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fans);

        return fans * MerchSpendMinor(level);
    }

    /// <summary>Lifts takings by a bonus, rounding down to a whole minor unit (`STRC-4`, `STRC-6`).</summary>
    /// <param name="baseMinor">The takings before the bonus.</param>
    /// <param name="bonusBp">The bonus in basis points.</param>
    public static long WithBonus(long baseMinor, int bonusBp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(bonusBp);

        return baseMinor * (OneHundredPercent + bonusBp) / OneHundredPercent;
    }

    /// <summary>The snack bar's takings for a home match, in minor units (`STRC-4`).</summary>
    /// <param name="level">The snack bar's level.</param>
    /// <param name="spectators">The crowd.</param>
    /// <param name="bonusBp">The result bonus, from <see cref="ResultBonusFor"/>.</param>
    public static long SnackTakingsMinorFor(int level, int spectators, int bonusBp) =>
        WithBonus(SnackBaseMinorFor(level, spectators), bonusBp);

    /// <summary>The merchandise shop's takings for a week, in minor units (`STRC-6`).</summary>
    /// <param name="level">The shop's level.</param>
    /// <param name="fans">The club's fans.</param>
    /// <param name="mood">The fans' mood word at the week's close.</param>
    public static long MerchTakingsMinorFor(int level, int fans, FanMoodWord mood) =>
        WithBonus(MerchBaseMinorFor(level, fans), MoodBonusBpFor(mood));

    /// <summary>
    /// What the snack bar would take in a week at a level, before any result bonus, in minor units: a quote for
    /// the page, not a booking (`STRC-4`).
    /// </summary>
    /// <param name="level">The snack bar's level.</param>
    /// <param name="spectators">The crowd the next home match draws.</param>
    /// <remarks>
    /// Matchdays fall three times a week and a club hosts half of them, so it hosts 1.5 matches a week on
    /// average.
    /// </remarks>
    public static long SnackWeeklyEstimateMinorFor(int level, int spectators) =>
        SnackBaseMinorFor(level, spectators) * 3 / 2;

    /// <summary>What the shop would take in a week at a level, in minor units: a quote for the page, not a booking (`STRC-6`).</summary>
    /// <param name="level">The shop's level.</param>
    /// <param name="fans">The club's fans.</param>
    /// <param name="mood">The fans' mood word now.</param>
    public static long MerchWeeklyEstimateMinorFor(int level, int fans, FanMoodWord mood) =>
        MerchTakingsMinorFor(level, fans, mood);

    private static int IndexOf(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, OpeningLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, MaxLevel);

        return level - 1;
    }

    private static int UpgradeIndexOf(int targetLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(targetLevel, OpeningLevel + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetLevel, MaxLevel);

        return targetLevel - 1;
    }
}

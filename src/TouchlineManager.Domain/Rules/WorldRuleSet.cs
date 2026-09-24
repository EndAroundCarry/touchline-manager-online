namespace TouchlineManager.Domain.Rules;

/// <summary>
/// The versioned world, occupancy, and calendar rule set (master plan §3; game rules §2–5, §13.3).
/// </summary>
/// <remarks>
/// <para>
/// The plan requires every configurable value from §3 to live in a versioned rule set rather than as
/// scattered magic constants, so this is the single place a balancing change is made. The version is
/// stamped onto the world and the season, which is what lets a historical season be interpreted with
/// the rules that were actually in force when it was played (game rules `TIME-3`, master plan §4.6).
/// </para>
/// <para>
/// Only the values Stage 3 depends on are here. Squad, tactics, training, discipline, contract, and
/// market constants arrive with their own stages; adding them earlier would be inventing behaviour
/// before it is specified.
/// </para>
/// </remarks>
public static class WorldRuleSet
{
    /// <summary>The rule-set version stamped onto every world and season created from it.</summary>
    public const string Version = "world-rules-v1";

    /// <summary>Every active division holds exactly 18 clubs (`WORLD-4`). There is no other size.</summary>
    public const int ClubsPerDivision = 18;

    /// <summary>A double round-robin season is 34 matchdays (`CAL-1`).</summary>
    public const int MatchdaysPerSeason = 34;

    /// <summary>Standard kickoff time, in UTC (`CAL-2`).</summary>
    public static readonly TimeOnly KickoffUtc = new(19, 0);

    /// <summary>The weekdays a matchday may fall on (`CAL-2`).</summary>
    public static readonly DayOfWeek[] KickoffWeekdays =
        [DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Sunday];

    /// <summary>Team sheets lock this many minutes before kickoff (`CAL-3`).</summary>
    public const int TeamSheetLockMinutes = 30;

    /// <summary>The rollover period between seasons, in days (`CAL-6`).</summary>
    public const int RolloverDays = 7;

    /// <summary>Clubs promoted from a lower tier at rollover (`PR-1`).</summary>
    public const int PromotedPerTier = 3;

    /// <summary>Clubs relegated from a higher tier at rollover (`PR-1`).</summary>
    public const int RelegatedPerTier = 3;

    /// <summary>Days without a login before an inactivity warning is sent (`OCC-1`).</summary>
    public static readonly TimeSpan InactivityWarningAfter = TimeSpan.FromDays(10);

    /// <summary>Days without a login before the tenure becomes inactive and AI assists (`OCC-2`).</summary>
    public static readonly TimeSpan InactivityAiAssistanceAfter = TimeSpan.FromDays(14);

    /// <summary>Days without a login before the tenure closes and the club returns to AI (`OCC-3`).</summary>
    public static readonly TimeSpan InactivityCloseAfter = TimeSpan.FromDays(21);

    /// <summary>The cooldown after a voluntary resignation before another takeover (`OCC-4`).</summary>
    public static readonly TimeSpan ResignationCooldown = TimeSpan.FromDays(7);

    /// <summary>The number of senior players the generator aims for per club (`SQ-1`).</summary>
    public const int GeneratorSquadTarget = 22;

    /// <summary>Opening cash for a tier-1 club, in minor units. A balancing value (see remarks).</summary>
    /// <remarks>
    /// The finance module owns the real economy in Stage 9; this exists so a seeded club has a fundable
    /// account rather than a zero balance that would make the first wage run fail. Lower tiers are
    /// scaled by <see cref="TierScalingFactor"/>, and both values are expected to be tuned from the
    /// multi-season simulations Stage 9 requires.
    /// </remarks>
    public const long OpeningCashMinorTier1 = 50_000_000;

    /// <summary>Opening stadium baseline for a tier-1 club, in minor units. A balancing value.</summary>
    public const long OpeningStadiumBaselineTier1 = 25_000_000;

    /// <summary>Opening reputation for a tier-1 club, on the same 1–100 scale as player ability.</summary>
    public const int OpeningReputationTier1 = 70;

    /// <summary>
    /// The divisor applied per tier below the first.
    /// </summary>
    /// <remarks>
    /// Halving per tier keeps a pyramid's money and stadium sizes monotonically decreasing with depth
    /// without introducing a table of magic numbers, and it degrades gracefully past tier 6 because
    /// the divisor is applied as a shift rather than a fixed lookup. It is deliberately crude: the
    /// shape matters for Stage 3, the calibration is Stage 9's job.
    /// </remarks>
    public static long TierScalingFactor(int tier)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "A division tier starts at 1 (WORLD-4).");
        }

        return 1L << (tier - 1);
    }

    /// <summary>Opening cash for a club in the given tier, in minor units.</summary>
    public static long OpeningCashMinorForTier(int tier) =>
        OpeningCashMinorTier1 / TierScalingFactor(tier);

    /// <summary>Opening stadium baseline for a club in the given tier, in minor units.</summary>
    public static long OpeningStadiumBaselineForTier(int tier) =>
        OpeningStadiumBaselineTier1 / TierScalingFactor(tier);

    /// <summary>Opening reputation for a club in the given tier.</summary>
    public static int OpeningReputationForTier(int tier) =>
        Math.Max(1, OpeningReputationTier1 / (int)TierScalingFactor(tier));
}

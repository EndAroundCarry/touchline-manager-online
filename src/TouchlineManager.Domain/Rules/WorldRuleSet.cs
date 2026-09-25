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
/// Constants arrive with the stage that needs them, never earlier, because adding a rule before it is
/// specified is inventing behaviour. Stage 3 contributed the world, occupancy, calendar, and finance
/// values; Stage 4 contributes the squad, contract, tactics, and training values below. Bumping
/// <see cref="Version"/> is what makes that a rule change rather than a silent constant tweak
/// (`RULE-3`); a world already stamped with an earlier version keeps being read against it.
/// </para>
/// </remarks>
public static class WorldRuleSet
{
    /// <summary>The rule-set version stamped onto every world and season created from it.</summary>
    public const string Version = "world-rules-v3";

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

    /// <summary>The number of goalkeepers the generator gives each club (`SQ-1`).</summary>
    /// <remarks>
    /// Three rather than the two `SQ-2` requires: a generated squad must survive a season of injuries and
    /// a suspension or two without falling below the minimum, and a squad generated at exactly the
    /// minimum would start one injury away from illegal.
    /// </remarks>
    public const int GeneratedGoalkeepers = 3;

    /// <summary>The number of defenders the generator gives each club (`SQ-1`).</summary>
    public const int GeneratedDefenders = 7;

    /// <summary>The number of midfielders the generator gives each club (`SQ-1`).</summary>
    public const int GeneratedMidfielders = 7;

    /// <summary>The number of attackers the generator gives each club (`SQ-1`).</summary>
    public const int GeneratedAttackers = 5;

    /// <summary>The smallest registered senior squad a club may field (`SQ-2`).</summary>
    public const int SquadMinimumRegistered = 18;

    /// <summary>The fewest goalkeepers a registered senior squad must contain (`SQ-2`).</summary>
    public const int MinimumGoalkeepers = 2;

    /// <summary>The largest registered senior squad a club may hold (`SQ-3`).</summary>
    public const int SquadMaximumRegistered = 25;

    /// <summary>Starters named on a match team sheet (`SQ-4`).</summary>
    public const int TeamSheetStarters = 11;

    /// <summary>Substitutes named on a match team sheet (`SQ-4`).</summary>
    public const int TeamSheetSubstitutes = 7;

    /// <summary>The lowest displayed attribute value (`TRN-4`).</summary>
    public const int AttributeMin = 1;

    /// <summary>The highest displayed attribute value (`TRN-4`).</summary>
    public const int AttributeMax = 20;

    /// <summary>The lowest value of a basis-point player-state measure (`TRN-5`…`TRN-7`).</summary>
    public const int StateBasisPointsMin = 0;

    /// <summary>The highest value of a basis-point player-state measure (`TRN-5`…`TRN-7`).</summary>
    public const int StateBasisPointsMax = 10_000;

    /// <summary>
    /// The UTC time the daily training progression runs (`TRN-3`).
    /// </summary>
    /// <remarks>
    /// A rule rather than an implementation detail, because it decides which day a manager's training
    /// choice is applied on. The materializer that enqueues the day's job reads it; the worker executes the
    /// row whenever it is claimed, so a delayed run is late rather than skipped (ADR-0003).
    /// </remarks>
    public static readonly TimeOnly DailyProgressionUtc = new(2, 0);

    /// <summary>The youngest game age a generated player may have.</summary>
    public const int PlayerMinimumAge = 17;

    /// <summary>The oldest game age a generated player may have.</summary>
    public const int PlayerMaximumAge = 34;

    /// <summary>The shortest contract the generator writes, in game seasons (`CON-1`).</summary>
    public const int ContractMinSeasons = 1;

    /// <summary>The longest contract the generator writes, in game seasons (`CON-1`).</summary>
    public const int ContractMaxSeasons = 3;

    /// <summary>The mean generated attribute for a tier-1 squad, on the 1–20 scale. A balancing value.</summary>
    /// <remarks>
    /// Provisional, like <see cref="OpeningCashMinorTier1"/>. The engine work in Stage 5 and the
    /// multi-season simulations in Stage 9 are what calibrate it; what matters now is that a generated
    /// squad reads as a coherent group rather than as noise.
    /// </remarks>
    public const int GeneratedAbilityMeanTier1 = 13;

    /// <summary>How far a squad's mean ability falls per tier below the first, in attribute points.</summary>
    /// <remarks>
    /// The finance and stadium baselines halve per tier because their shape is multiplicative, but
    /// ability lives on a 1–20 display scale, where halving would hit the floor by tier 3 and make every
    /// deep tier identical. An additive drop keeps the tiers ordered without collapsing them.
    /// </remarks>
    public const int GeneratedAbilityDropPerTier = 2;

    /// <summary>The lowest slot coordinate on a tactical pitch's scaled axis (`TAC-9`).</summary>
    public const int SlotCoordinateMin = 0;

    /// <summary>The highest slot coordinate on a tactical pitch's scaled axis (`TAC-9`).</summary>
    public const int SlotCoordinateMax = 10_000;

    /// <summary>
    /// The wage scale the generator uses, in minor units per unit of squared ability. A balancing value.
    /// </summary>
    /// <remarks>
    /// Wages are quadratic in ability so a squad's payroll is dominated by its best players rather than
    /// spread evenly, which is the shape Stage 9's wage run and Stage 10's market need. The absolute
    /// level is provisional and is calibrated by the multi-season simulations, like the baselines above.
    /// </remarks>
    public const long GeneratedWeeklyWageMinorPerAbilitySquared = 300;

    /// <summary>The weekly wage a generated player is signed on, in minor units.</summary>
    /// <param name="ability">The player's mean ability on the 1–20 scale.</param>
    /// <param name="tier">The tier the club plays in, which scales the wage like the other baselines.</param>
    public static long GeneratedWeeklyWageMinorFor(int ability, int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ability, AttributeMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ability, AttributeMax);

        return (long)ability * ability
            * GeneratedWeeklyWageMinorPerAbilitySquared
            / TierScalingFactor(tier);
    }

    /// <summary>The mean generated attribute for a squad in the given tier.</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int GeneratedAbilityMeanForTier(int tier)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "A division tier starts at 1 (WORLD-4).");
        }

        return Math.Clamp(
            GeneratedAbilityMeanTier1 - (GeneratedAbilityDropPerTier * (tier - 1)),
            AttributeMin,
            AttributeMax);
    }
}

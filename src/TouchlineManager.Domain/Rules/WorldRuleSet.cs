using TouchlineManager.Domain.Squad;

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
/// values; Stage 4 the squad, contract, tactics, and training values; Stage 6 the schedule-streak bound
/// the fixture generator validates against; Stage 8 the injury and suspension bands the match effects
/// apply; Stage 9 the gate, sponsorship, operating-cost, award, and payroll-risk values the finance runs
/// settle; Stage 10 the auction windows, blackout, and minimum bid increment the transfer market runs on,
/// and — its AI-market milestone — the player valuation and bidding bands the AI's own market decisions
/// use (`TRF-12`).
/// Stage 12 added the rollover-continuity values: the contract-continuity AI's target squad size, and the
/// retirement rule's start age, growth, forced caps, and ability and fitness gate (`CON-6`, `CON-8`).
/// Bumping <see cref="Version"/> is what makes that a rule change rather than a silent constant
/// tweak (`RULE-3`); a world already stamped with an earlier version keeps being read against it.
/// </para>
/// </remarks>
public static class WorldRuleSet
{
    /// <summary>The rule-set version stamped onto every world and season created from it.</summary>
    public const string Version = "world-rules-v9";

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

    /// <summary>
    /// The longest run of consecutive home or consecutive away fixtures a generated schedule may contain
    /// (`CAL-9`).
    /// </summary>
    /// <remarks>
    /// A bound rather than a target, and it is four rather than three because four is what the generator's
    /// schedule actually produces: mirroring a round-robin half always leaves some runs, and a four-match
    /// home stand is a normal feature of a real fixture list rather than something to engineer away. The
    /// property tests assert this bound across every plausible division size and fifty seeds, so it is the
    /// schedule's demonstrated contract rather than an aspiration.
    /// </remarks>
    public const int MaxConsecutiveHomeOrAway = 4;

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

    /// <summary>
    /// How long before a matchday's team-sheet lock the deadline reminder is sent (`COM-3`).
    /// </summary>
    /// <remarks>
    /// A non-balancing notification constant: it decides when a manager is reminded, not what happens in a
    /// match, so adding it does not bump <see cref="Version"/> (ADR-0029).
    /// </remarks>
    public static readonly TimeSpan DeadlineReminderLead = TimeSpan.FromHours(24);

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

    /// <summary>
    /// The weekly finance boundary: Sunday evening, after the Sunday matchday has been played (`CON-2`).
    /// </summary>
    /// <remarks>
    /// Wages are charged weekly, "after the Sunday matchday" (`CON-2`), so the run is anchored to Sunday at
    /// 23:00 UTC — the last of the three kickoff days, and late enough that the round it follows has
    /// published. A rule rather than an implementation detail, because it decides which day a club is paid
    /// and therefore what a manager sees in the ledger. The materialiser that enqueues the run reads it; the
    /// worker executes the row whenever it is claimed, so a delayed run is late rather than skipped
    /// (ADR-0003).
    /// </remarks>
    public static readonly TimeOnly WeeklyFinanceUtc = new(23, 0);

    /// <summary>
    /// The fraction of the stadium baseline a full house yields, in basis points (`FIN-3`).
    /// </summary>
    /// <remarks>
    /// Gate revenue is a share of the club's own fixed stadium baseline rather than an invented attendance
    /// figure, so it scales with tier the same way the baseline does. The value is provisional and is
    /// calibrated by the multi-season simulations this stage requires, like the other baselines.
    /// </remarks>
    public const int GateRevenueBaseFractionBp = 2_000;

    /// <summary>How much one place in the table moves the gate factor, in basis points (`FIN-3`).</summary>
    /// <remarks>Above and below the middle place the factor rises and falls, and both ends clamp.</remarks>
    private const int GateRevenueFormFactorStepPerRankBp = 300;

    /// <summary>
    /// The attendance factor a league position applies to gate revenue, in basis points (`FIN-3`).
    /// </summary>
    /// <param name="formRank">The club's league position, 1 (top) to 18 (bottom).</param>
    /// <returns>A factor bounded between 8,000 and 12,000 basis points, pivoting on ninth place.</returns>
    public static int GateRevenueFormFactorBpFor(int formRank)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(formRank, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(formRank, ClubsPerDivision);

        return Math.Clamp(10_000 + ((9 - formRank) * GateRevenueFormFactorStepPerRankBp), 8_000, 12_000);
    }

    /// <summary>The gate revenue a home fixture yields, in minor units (`FIN-3`).</summary>
    /// <param name="stadiumBaseline">The club's fixed stadium baseline, already scaled to its tier.</param>
    /// <param name="formRank">The club's league position, 1–18.</param>
    public static long GateRevenueMinorFor(long stadiumBaseline, int formRank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stadiumBaseline);

        // Divided in two steps so the intermediate product stays well inside long, and so the factor and the
        // fraction each truncate on their own rather than blending into one rounding.
        return stadiumBaseline
            * GateRevenueBaseFractionBp / 10_000
            * GateRevenueFormFactorBpFor(formRank) / 10_000;
    }

    /// <summary>The weekly sponsorship credit for a tier-1 club, in minor units. A balancing value (`FIN-4`).</summary>
    public const long WeeklySponsorshipMinorTier1 = 3_000_000;

    /// <summary>The weekly sponsorship credit for a club in the given tier, in minor units (`FIN-4`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long WeeklySponsorshipMinorForTier(int tier) =>
        WeeklySponsorshipMinorTier1 / TierScalingFactor(tier);

    /// <summary>The small fixed weekly operating cost for a tier-1 club, in minor units (`FIN-9`).</summary>
    public const long WeeklyOperatingCostMinorTier1 = 1_000_000;

    /// <summary>The small fixed weekly operating cost for a club in the given tier, in minor units (`FIN-9`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long WeeklyOperatingCostMinorForTier(int tier) =>
        WeeklyOperatingCostMinorTier1 / TierScalingFactor(tier);

    /// <summary>The final-position award for the champion of a tier-1 division, in minor units (`FIN-5`).</summary>
    public const long PositionAwardMinorTier1Winner = 40_000_000;

    /// <summary>The award a club earns for finishing at a rank in a tier, in minor units (`FIN-5`).</summary>
    /// <remarks>
    /// The award decays linearly with position — the champion takes the full amount, the bottom club a
    /// minimum share — and the whole scale halves per tier like the other baselines. Settled at rollover.
    /// </remarks>
    /// <param name="tier">The tier number, starting at 1.</param>
    /// <param name="rank">The club's final position, 1–18.</param>
    public static long PositionAwardMinorFor(int tier, int rank)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rank, ClubsPerDivision);

        var baseAward = PositionAwardMinorTier1Winner / TierScalingFactor(tier);
        var share = ClubsPerDivision - (rank - 1);

        return baseAward * share / ClubsPerDivision;
    }

    /// <summary>
    /// How many weeks of its wage bill a club must hold in cash before payroll is not a risk (`FIN-16`).
    /// </summary>
    /// <remarks>
    /// The threshold the dashboards warn on and the safety job watches: a club with less than this many
    /// weeks of wages in cash is one bad week away from failing to pay, which is the condition the emergency
    /// grant exists to prevent. A balancing value, tuned out through the multi-season simulations.
    /// </remarks>
    public const int PayrollRiskWeeks = 4;

    /// <summary>
    /// How a player's age shapes a renewal quote, in basis points (`CON-3`).
    /// </summary>
    /// <remarks>
    /// Young players are cheaper to tie down and old ones cheaper still, with the peak years the middle.
    /// Bounded by construction, so no age can multiply a wage into a number the base does not support. A
    /// balancing value, like the other finance baselines.
    /// </remarks>
    /// <param name="age">The player's age in game years.</param>
    public static int RenewalAgeFactorBp(int age)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);

        return age switch
        {
            <= 23 => 11_500,
            <= 28 => 10_500,
            <= 31 => 9_500,
            _ => 8_500,
        };
    }

    /// <summary>How many appearances a player has made this season shapes their renewal quote (`CON-3`).</summary>
    /// <param name="appearances">Matches played this season.</param>
    public static int RenewalAppearancesFactorBp(int appearances)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(appearances);

        return appearances switch
        {
            >= 20 => 11_000,
            >= 10 => 10_500,
            >= 1 => 10_000,
            _ => 9_500,
        };
    }

    /// <summary>How morale shapes a renewal quote, in basis points (`CON-3`).</summary>
    /// <param name="moraleBp">The player's morale in basis points (`TRN-7`).</param>
    public static int RenewalMoraleFactorBp(int moraleBp)
    {
        if (moraleBp is < StateBasisPointsMin or > StateBasisPointsMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(moraleBp),
                moraleBp,
                $"Morale is between {StateBasisPointsMin} and {StateBasisPointsMax} basis points (TRN-7).");
        }

        // 9,000 at rock bottom, 10,000 at the ceiling: an unhappy player needs the sweeter offer.
        return 9_000 + (moraleBp / 10);
    }

    /// <summary>How the length of the new deal shapes a renewal quote, in basis points (`CON-3`).</summary>
    /// <param name="seasons">The new contract's length, 1–3 seasons (`CON-1`).</param>
    public static int RenewalTermFactorBp(int seasons)
    {
        if (seasons is < ContractMinSeasons or > ContractMaxSeasons)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seasons),
                seasons,
                $"A contract is between {ContractMinSeasons} and {ContractMaxSeasons} game seasons (CON-1).");
        }

        // A longer commitment buys a slightly lower weekly wage.
        return seasons switch
        {
            1 => 10_500,
            2 => 10_000,
            _ => 9_500,
        };
    }

    /// <summary>How much of the current term is left shapes a renewal quote (`CON-3`).</summary>
    /// <param name="remainingSeasons">Full seasons left after the current one.</param>
    public static int RenewalRemainingTermFactorBp(int remainingSeasons)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(remainingSeasons);

        // A player whose deal is running out holds the stronger hand, so the expiring year costs the most.
        return remainingSeasons switch
        {
            0 => 11_000,
            1 => 10_500,
            _ => 10_000,
        };
    }

    /// <summary>The age at which a player may first announce a retirement (`CON-6`, `CON-8`).</summary>
    /// <remarks>
    /// A hidden mechanic: a manager knows that from this age a player <em>may</em> announce that the coming
    /// season is their last, and that the chance grows with age, but never by how much. Only the announcement
    /// itself — a fact about the coming season — is ever surfaced.
    /// </remarks>
    public const int RetirementAnnouncementStartAge = 32;

    /// <summary>The chance a player of the start age announces a retirement, in per-mille (`CON-6`).</summary>
    public const int RetirementAnnouncementBaseChancePerMille = 300;

    /// <summary>How much each season past the start age adds to the announcement chance, in per-mille.</summary>
    public const int RetirementAnnouncementStepPerSeasonPerMille = 150;

    /// <summary>The chance ceiling in per-mille, so age alone can never force a random announcement.</summary>
    public const int RetirementAnnouncementCeilingPerMille = 900;

    /// <summary>The entry age at which an outfielder is forced to announce a retirement (`CON-6`).</summary>
    public const int RetirementForcedAnnouncementAgeOutfield = 36;

    /// <summary>The entry age at which a goalkeeper is forced to announce a retirement (`CON-6`).</summary>
    public const int RetirementForcedAnnouncementAgeGoalkeeper = 38;

    /// <summary>The entry age at which an outfielder is forced out of the game (`CON-6`).</summary>
    public const int RetirementForcedAgeOutfield = 37;

    /// <summary>The entry age at which a goalkeeper is forced out of the game (`CON-6`).</summary>
    public const int RetirementForcedAgeGoalkeeper = 39;

    /// <summary>How a player's ability shapes the announcement chance, in basis points (`CON-6`).</summary>
    /// <remarks>
    /// A better player is less likely to announce, which is what lets only very good players reach the forced
    /// cap while the rest retire in their early thirties. Bounded, like every other factor.
    /// </remarks>
    /// <param name="ability">The player's ability on the 1–20 scale.</param>
    public static int RetirementAbilityFactorBp(int ability)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ability, AttributeMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ability, AttributeMax);

        return ability switch
        {
            >= 16 => 6_500,
            >= 14 => 8_000,
            >= 12 => 10_000,
            >= 10 => 12_000,
            _ => 14_000,
        };
    }

    /// <summary>How a player's condition shapes the announcement chance, in basis points (`CON-6`).</summary>
    /// <remarks>
    /// "Fitness" is read as the player's current condition; a fresh player is less likely to announce, so a
    /// very fit one reaches the cap. Bounded, like every other factor.
    /// </remarks>
    /// <param name="conditionBp">The player's condition in basis points (`TRN-5`).</param>
    public static int RetirementConditionFactorBp(int conditionBp)
    {
        if (conditionBp is < StateBasisPointsMin or > StateBasisPointsMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(conditionBp),
                conditionBp,
                $"Condition is between {StateBasisPointsMin} and {StateBasisPointsMax} basis points (TRN-5).");
        }

        return conditionBp switch
        {
            >= 8_000 => 8_500,
            >= 5_000 => 10_000,
            _ => 12_000,
        };
    }

    /// <summary>The chance a player announces a retirement this rollover, in per-mille (`CON-6`).</summary>
    /// <param name="age">The age the player would enter the coming season at.</param>
    /// <param name="ability">The player's ability on the 1–20 scale.</param>
    /// <param name="conditionBp">The player's condition in basis points.</param>
    public static int RetirementAnnouncementChancePerMille(int age, int ability, int conditionBp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);

        if (age < RetirementAnnouncementStartAge)
        {
            return 0;
        }

        var baseChance = RetirementAnnouncementBaseChancePerMille
            + ((age - RetirementAnnouncementStartAge) * RetirementAnnouncementStepPerSeasonPerMille);

        var gated = baseChance
            * RetirementAbilityFactorBp(ability) / 10_000
            * RetirementConditionFactorBp(conditionBp) / 10_000;

        return Math.Clamp(gated, 0, RetirementAnnouncementCeilingPerMille);
    }

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

    /// <summary>The league yellow cards that trigger a one-match suspension (`DIS-2`).</summary>
    public const int YellowSuspensionThreshold = 5;

    /// <summary>The fixtures a yellow-accumulation suspension costs (`DIS-2`).</summary>
    public const int YellowSuspensionFixtures = 1;

    /// <summary>The fixtures a sending-off costs in MVP rules (`DIS-4`).</summary>
    public const int RedCardSuspensionFixtures = 1;

    /// <summary>The shortest absence an injury causes, in eligible fixtures (`DIS-1`).</summary>
    public const int MinInjuryAbsenceFixtures = 1;

    /// <summary>The longest absence an injury causes, in eligible fixtures (`DIS-1`).</summary>
    public const int MaxInjuryAbsenceFixtures = 6;

    /// <summary>The longest absence a minor injury causes, in eligible fixtures (`DIS-1`).</summary>
    public const int MinorInjuryMaxAbsenceFixtures = 2;

    /// <summary>The longest absence a moderate injury causes, in eligible fixtures (`DIS-1`).</summary>
    public const int ModerateInjuryMaxAbsenceFixtures = 4;

    /// <summary>
    /// Maps an injury's fixture absence to the band that describes it (`DIS-1`).
    /// </summary>
    /// <remarks>
    /// The rule set owns the bands rather than the engine: the engine draws an absence between one and six
    /// fixtures, and what those numbers mean to a manager — a knock, a spell out, a long lay-off — is
    /// game-rules vocabulary that belongs here, where the discipline and injury stages read it.
    /// </remarks>
    /// <param name="absenceFixtures">The absence the injury causes, in eligible fixtures.</param>
    /// <returns>The severity band the absence falls in.</returns>
    public static InjurySeverity InjurySeverityFor(int absenceFixtures)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(absenceFixtures, MinInjuryAbsenceFixtures);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(absenceFixtures, MaxInjuryAbsenceFixtures);

        return absenceFixtures <= MinorInjuryMaxAbsenceFixtures
            ? InjurySeverity.Minor
            : absenceFixtures <= ModerateInjuryMaxAbsenceFixtures
                ? InjurySeverity.Moderate
                : InjurySeverity.Major;
    }

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

    /// <summary>
    /// The UTC time the daily transfer-auction resolution window opens (`TRF-2`, `TRF-3`).
    /// </summary>
    /// <remarks>
    /// One fixed window a day, at midday, which sits outside the 13:00–19:00 UTC blackout
    /// <see cref="AuctionBlackoutHoursBeforeKickoff"/> creates around the standard 19:00 kickoff. A rule
    /// rather than an implementation detail, because it decides when a manager's bid becomes a transfer. The
    /// materialiser that enqueues a listing's resolution job reads it; the worker executes the row whenever it
    /// is claimed, so a delayed resolution is late rather than skipped (ADR-0003).
    /// </remarks>
    public static readonly TimeOnly AuctionResolutionUtc = new(12, 0);

    /// <summary>The least time a listing must be open before it may resolve, in hours (`TRF-2`).</summary>
    public const int ListingMinimumExposureHours = 48;

    /// <summary>How long before a matchday kickoff auction resolution is blacked out, in hours (`TRF-3`).</summary>
    /// <remarks>
    /// With the standard 19:00 UTC kickoff this is the 13:00–19:00 UTC window on the three kickoff days, so a
    /// transfer never lands in the hours a manager is finalising a team sheet for the round.
    /// </remarks>
    public const int AuctionBlackoutHoursBeforeKickoff = 6;

    /// <summary>
    /// The least a bid must exceed the current leading bid by, in minor units (`TRF-5`). A balancing value.
    /// </summary>
    /// <remarks>
    /// A flat increment rather than a percentage: it guarantees every raise makes progress, and its absolute
    /// level is tuned from the market-health measurements this stage requires like the other baselines.
    /// </remarks>
    public const long MinimumBidIncrementMinor = 250_000;

    /// <summary>The most characters a private shortlist note may hold (`SCT-3`).</summary>
    public const int ShortlistNotesMaxLength = 280;

    /// <summary>
    /// The number of weeks of a player's wage their transfer value is worth (`TRF-12`). A balancing value.
    /// </summary>
    /// <remarks>
    /// The valuation is deliberately derived from the wage scale the generator and the renewal quote
    /// already use rather than from a second, disconnected money scale: a squad, a renewal, and a fee are
    /// then priced by one family of rules, and the AI's asking price and bid ceiling cannot drift away from
    /// what a player actually costs to keep.
    /// </remarks>
    public const long PlayerValuationWeeksOfWage = 60;

    /// <summary>How a player's age shapes their transfer value, in basis points (`TRF-12`).</summary>
    /// <remarks>
    /// Younger players carry resale and development value, the peak years are the premium, and a veteran is
    /// cheap. Bounded, like every other factor, so no age can multiply a value into a number the wage scale
    /// does not support.
    /// </remarks>
    /// <param name="age">The player's age in game years.</param>
    public static int PlayerValuationAgeFactorBp(int age)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);

        return age switch
        {
            <= 21 => 13_000,
            <= 24 => 12_000,
            <= 28 => 10_500,
            <= 31 => 8_500,
            _ => 6_000,
        };
    }

    /// <summary>How a player's hidden potential shapes their transfer value, in basis points (`TRF-12`).</summary>
    /// <remarks>
    /// A higher ceiling commands more, bounded between 9,000 and 12,000 basis points so potential moves a
    /// value without dominating the ability it is measured beside.
    /// </remarks>
    /// <param name="potential">The hidden development ceiling, on the 1–20 scale (`TRN-9`).</param>
    public static int PlayerValuationPotentialFactorBp(int potential)
    {
        if (potential is < AttributeMin or > AttributeMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(potential),
                potential,
                $"Potential is between {AttributeMin} and {AttributeMax} (TRN-4).");
        }

        return 9_000 + (potential * 150);
    }

    /// <summary>
    /// The registration count a club's position family is expected to hold (`SQ-1`), which is where the AI
    /// market reads "surplus" from (`TRF-12`).
    /// </summary>
    /// <remarks>
    /// The generator's own quotas, reused rather than restated: a club above a family's quota is holding
    /// more of that position than a balanced squad needs, so that family is where a surplus player may be
    /// found. Using the same numbers the generator builds to keeps "surplus" a fact about the squad's shape
    /// rather than a second opinion about it.
    /// </remarks>
    /// <param name="family">The position family.</param>
    public static int AiMarketFamilyCap(PositionFamily family) => family switch
    {
        PositionFamily.Goalkeeper => GeneratedGoalkeepers,
        PositionFamily.Defence => GeneratedDefenders,
        PositionFamily.Midfield => GeneratedMidfielders,
        PositionFamily.Attack => GeneratedAttackers,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown position family."),
    };

    /// <summary>
    /// The squad size an AI club trims toward when it lists a surplus player (`TRF-12`).
    /// </summary>
    /// <remarks>
    /// One below the generator's target, so a generated squad of twenty-two has exactly one player the AI
    /// would move on and stays a manageable size without ever approaching the minimum `SQ-2` requires.
    /// </remarks>
    public const int AiMarketTargetSquadSize = 21;

    /// <summary>
    /// The squad size a club nobody manages renews toward at rollover (`CON-6`, `CON-8`).
    /// </summary>
    /// <remarks>
    /// The same target the AI market trims toward, so an unmanaged club neither grows past what the market
    /// would sell nor falls toward the minimum: it renews its best players up to this size and lets the
    /// surplus go to free agency, keeping a legal squad without hoarding.
    /// </remarks>
    public const int AiContractTargetSquadSize = 21;

    /// <summary>The most players an AI club lists in one evaluation, so a pass cannot flood the market (`TRF-12`).</summary>
    public const int AiMarketMaxListingsPerClub = 3;

    /// <summary>The most listings an AI club bids on in one evaluation, so a pass cannot over-commit it (`TRF-12`).</summary>
    public const int AiMarketMaxBidsPerClub = 3;

    /// <summary>
    /// The share of its available cash an AI club will commit to a single bid, in basis points (`TRF-12`).
    /// </summary>
    /// <remarks>
    /// A bid is also capped by the player's valuation, which usually binds first for a solvent club; this
    /// floor is what stops a cash-poor AI club from spending money it needs for the next wage run. A
    /// balancing value, tuned from the market-health measurements this milestone requires.
    /// </remarks>
    public const int AiMarketBidBudgetFractionBp = 5_000;
}

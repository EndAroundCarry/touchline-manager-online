using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Rules;

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
/// The stadium milestone moved gate revenue off a fixed baseline and onto the club's own ground — places,
/// prices, and the crowd that fills them — which lives in <see cref="StadiumRuleSet"/> (`STAD-*`).
/// Stage 12 added the rollover-continuity values: the contract-continuity AI's target squad size, and the
/// retirement rule's start age, growth, forced caps, and ability and fitness gate (`CON-6`, `CON-8`).
/// The wide-midfielder milestone grew the generated squad from twenty-two to twenty-four (one left and one
/// right midfielder) and the registration ceiling from twenty-five to twenty-seven (`SQ-1`, `SQ-3`).
/// The structures milestone added the snack bar and the merchandise shop, whose levels, takings, bonuses and
/// upkeep live in <see cref="StructureRuleSet"/> (`STRC-*`).
/// The economy rework moved the money rules (what a player is worth and paid, what a club earns, what a season pays) into
/// <see cref="EconomyRuleSet"/>; the entries below that were tier-divided baselines now read from it.
/// Bumping <see cref="Version"/> is what makes that a rule change rather than a silent constant
/// tweak (`RULE-3`); a world already stamped with an earlier version keeps being read against it.
/// </para>
/// </remarks>
public static class WorldRuleSet
{
    /// <summary>The rule-set version stamped onto every world and season created from it.</summary>
    public const string Version = "world-rules-v17";

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
    public const int GeneratorSquadTarget = 24;

    /// <summary>Opening cash for a tier-1 club, in minor units. A balancing value; see <see cref="EconomyRuleSet.OpeningCashMinorTier1"/>.</summary>
    public const long OpeningCashMinorTier1 = EconomyRuleSet.OpeningCashMinorTier1;

    /// <summary>Opening cash for a club in the given tier, in minor units.</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long OpeningCashMinorForTier(int tier) => EconomyRuleSet.OpeningCashMinorFor(tier);

    /// <summary>Opening reputation (standing) for a club in the given tier: 50 at tier 1, 10 less for each tier below (`STD-1`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int OpeningReputationForTier(int tier) => EconomyRuleSet.OpeningStandingFor(tier);

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

    /// <summary>The date of the weekly finance boundary that settles an instant: the first Sunday 23:00 UTC at or after it (`CON-2`).</summary>
    /// <param name="instant">The instant, such as a fixture's kickoff.</param>
    /// <remarks>
    /// The weekly run keys its postings by this date, so a home match's snack bar takings and the same week's
    /// upkeep line up in the structure history (`STRC-4`, `STRC-8`).
    /// </remarks>
    public static DateOnly WeeklyFinanceDateFor(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        var daysToSunday = ((int)DayOfWeek.Sunday - (int)utc.DayOfWeek + 7) % 7;
        var date = DateOnly.FromDateTime(utc).AddDays(daysToSunday);

        return daysToSunday == 0 && TimeOnly.FromDateTime(utc) >= WeeklyFinanceUtc
            ? date.AddDays(7)
            : date;
    }

    /// <summary>The league and sponsor money a weekly run credits a club in the given tier, in minor units (`FIN-4`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    /// <remarks>
    /// The league's television money and the steady sponsor's fixed part, three matchdays' worth (`CON-2`). The weekly run goes when the
    /// matchday settles them (M3); until then this is what the run credits.
    /// </remarks>
    public static long WeeklySponsorshipMinorForTier(int tier) =>
        EconomyRuleSet.PerWeeklyRun(EconomyRuleSet.LeagueTvSeasonMinorFor(tier) + EconomyRuleSet.SponsorBaseSeasonMinorFor(tier));

    /// <summary>The small fixed operating cost a weekly run charges a club in the given tier, in minor units (`FIN-9`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static long WeeklyOperatingCostMinorForTier(int tier) =>
        EconomyRuleSet.PerWeeklyRun(EconomyRuleSet.OperatingCostSeasonMinorFor(tier));

    /// <summary>The prize a club earns for finishing at a rank in a tier, in minor units (`FIN-5`).</summary>
    /// <remarks>A straight line down the table with bigger steps for the top three; see <see cref="EconomyRuleSet.PrizeMinorFor"/>.</remarks>
    /// <param name="tier">The tier number, starting at 1.</param>
    /// <param name="rank">The club's final position, 1–18.</param>
    public static long PositionAwardMinorFor(int tier, int rank) => EconomyRuleSet.PrizeMinorFor(tier, rank);

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

        // A longer commitment buys a slightly lower wage.
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

    /// <summary>
    /// The number of midfielders the generator gives each club (`SQ-1`): two defensive, three central, two
    /// attacking, and one left and one right midfielder.
    /// </summary>
    public const int GeneratedMidfielders = 9;

    /// <summary>The number of attackers the generator gives each club (`SQ-1`).</summary>
    public const int GeneratedAttackers = 5;

    /// <summary>The smallest registered senior squad a club may field (`SQ-2`).</summary>
    public const int SquadMinimumRegistered = 18;

    /// <summary>The fewest goalkeepers a registered senior squad must contain (`SQ-2`).</summary>
    public const int MinimumGoalkeepers = 2;

    /// <summary>The largest registered senior squad a club may hold (`SQ-3`).</summary>
    /// <remarks>
    /// Three above the generator's target of twenty-four, the headroom for a transfer or a youth signing that
    /// a squad of twenty-two had under the earlier limit of twenty-five.
    /// </remarks>
    public const int SquadMaximumRegistered = 27;

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
    /// Provisional, like <see cref="EconomyRuleSet.OpeningCashMinorTier1"/>. The engine work in Stage 5 and the
    /// multi-season simulations in Stage 9 are what calibrate it; what matters now is that a generated
    /// squad reads as a coherent group rather than as noise.
    /// </remarks>
    public const int GeneratedAbilityMeanTier1 = 13;

    /// <summary>The share of generated players who are gems, in percent (`TRN-18`). A balancing value.</summary>
    public const int GeneratedPotentialGemPercent = 5;

    /// <summary>The share of generated players who are talents, in percent (`TRN-18`). A balancing value.</summary>
    public const int GeneratedPotentialTalentPercent = 15;

    /// <summary>
    /// The mean potential of a youth-intake player at a tier-1 club, on the 1–20 scale (`TRN-18`). A balancing
    /// value.
    /// </summary>
    /// <remarks>
    /// Its own constant, equal to <see cref="GeneratedAbilityMeanTier1"/> today, so reseeding the starting
    /// world at a lower mean never lowers the talent that later intakes can reach. The bands around it are the
    /// generated players' own (<see cref="GeneratedPotentialGemPercent"/> and the offsets below).
    /// </remarks>
    public const int YouthPotentialMeanTier1 = 13;

    /// <summary>How far a youth intake's mean potential falls per tier below the first (`TRN-18`).</summary>
    public const int YouthPotentialDropPerTier = 2;

    /// <summary>
    /// The lowest a normal player's potential sits from his tier's mean ability. The normal offset is
    /// two dice of <see cref="GeneratedPotentialNormalDieFaces"/> faces added together, less this
    /// (`TRN-18`). A balancing value.
    /// </summary>
    public const int GeneratedPotentialNormalFloor = -4;

    /// <summary>The faces on each of the two dice that make a normal player's potential offset (`TRN-18`).</summary>
    public const int GeneratedPotentialNormalDieFaces = 4;

    /// <summary>The lowest offset of a talent's potential from his tier's mean ability (`TRN-18`).</summary>
    public const int GeneratedPotentialTalentOffsetMin = 3;

    /// <summary>The number of offsets a talent can draw, counting up from the minimum (`TRN-18`).</summary>
    public const int GeneratedPotentialTalentOffsetCount = 3;

    /// <summary>The lowest offset of a gem's potential from his tier's mean ability (`TRN-18`).</summary>
    public const int GeneratedPotentialGemOffsetMin = 6;

    /// <summary>The number of offsets a gem can draw, counting up from the minimum (`TRN-18`).</summary>
    public const int GeneratedPotentialGemOffsetCount = 2;

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

    /// <summary>The mean potential of a youth-intake player at a club in the given tier (`TRN-18`).</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int YouthPotentialMeanForTier(int tier)
    {
        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "A division tier starts at 1 (WORLD-4).");
        }

        return Math.Clamp(
            YouthPotentialMeanTier1 - (YouthPotentialDropPerTier * (tier - 1)),
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
    /// One below the generator's target, so a generated squad of twenty-four has exactly one player the AI
    /// would move on and stays a manageable size without ever approaching the minimum `SQ-2` requires.
    /// </remarks>
    public const int AiMarketTargetSquadSize = 23;

    /// <summary>
    /// The squad size a club nobody manages renews toward at rollover (`CON-6`, `CON-8`).
    /// </summary>
    /// <remarks>
    /// The same target the AI market trims toward, so an unmanaged club neither grows past what the market
    /// would sell nor falls toward the minimum: it renews its best players up to this size and lets the
    /// surplus go to free agency, keeping a legal squad without hoarding. It was 21 while the market's was 23,
    /// which took a generated squad of 24 down to 21 at its first rollover and, with the AI barred from the
    /// free-agent pool, left it there; the two targets are now one number (`CON-9`).
    /// </remarks>
    public const int AiContractTargetSquadSize = 23;

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

    /// <summary>
    /// The size of a weekly free-agent batch, as a share of the players under active contract, in basis
    /// points (`FA-1`). A balancing value.
    /// </summary>
    public const int FreeAgentShareBp = 1_000;

    /// <summary>The share of a generated batch that plays in a position family, in basis points (`FA-2`).</summary>
    /// <remarks>The four shares sum to 10,000: 15% goalkeepers, 28% defenders, 29% midfielders, 28% attackers.</remarks>
    /// <param name="family">The position family.</param>
    public static int FreeAgentFamilyShareBp(PositionFamily family) => family switch
    {
        PositionFamily.Goalkeeper => 1_500,
        PositionFamily.Defence => 2_800,
        PositionFamily.Midfield => 2_900,
        PositionFamily.Attack => 2_800,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown position family."),
    };

    /// <summary>The lowest displayed ability a generated free agent shows, on the 1–20 scale (`FA-3`).</summary>
    public const int FreeAgentAbilityMin = 6;

    /// <summary>The highest displayed ability a generated free agent shows, on the 1–20 scale (`FA-3`).</summary>
    public const int FreeAgentAbilityMax = 10;

    /// <summary>The youngest age of a normal generated free agent (`FA-3`).</summary>
    public const int FreeAgentAgeMin = 20;

    /// <summary>The oldest age of a normal generated free agent (`FA-3`).</summary>
    public const int FreeAgentAgeMax = 32;

    /// <summary>The highest hidden potential a normal generated free agent can have (`FA-3`).</summary>
    public const int FreeAgentPotentialCap = 11;

    /// <summary>The most points of headroom over his ability a young normal free agent can have (`FA-3`).</summary>
    public const int FreeAgentHeadroomMax = 2;

    /// <summary>The oldest age that still has headroom; older free agents have a ceiling at their ability (`FA-3`).</summary>
    public const int FreeAgentHeadroomAgeMax = 23;

    /// <summary>The share of generated free agents who are hidden gems, in per-mille (`FA-4`). A balancing value.</summary>
    public const int FreeAgentGemPermille = 10;

    /// <summary>The oldest age of a hidden gem; the youngest is <see cref="PlayerMinimumAge"/> (`FA-4`).</summary>
    public const int FreeAgentGemAgeMax = 21;

    /// <summary>The lowest hidden potential of a gem (`FA-4`).</summary>
    public const int FreeAgentGemPotentialMin = 13;

    /// <summary>The highest hidden potential of a gem (`FA-4`).</summary>
    public const int FreeAgentGemPotentialMax = 16;

    /// <summary>
    /// A free agent's minimum fee as a share of his valuation, in basis points (`FA-6`). A balancing value.
    /// </summary>
    /// <remarks>Never below <see cref="MinimumBidIncrementMinor"/>, so the fee is always a real price.</remarks>
    public const int FreeAgentMinimumFeeBp = 1_500;

    /// <summary>
    /// A released player's minimum fee as a share of his valuation, in basis points (`FA-6`). A balancing value.
    /// </summary>
    /// <remarks>
    /// Full value, with his real potential, like a club listing's asking price: a released player is a former
    /// club player, not cheap depth. Never below <see cref="MinimumBidIncrementMinor"/>.
    /// </remarks>
    public const int FreeAgentReleasedFeeBp = 10_000;

    /// <summary>The weekday a new free-agent batch opens, at <see cref="AuctionResolutionUtc"/> (`FA-5`).</summary>
    public const DayOfWeek FreeAgentWeekStartDay = DayOfWeek.Monday;

    /// <summary>
    /// The length of the contract a free agent signs, in game seasons, from his age alone (`FA-7`).
    /// </summary>
    /// <remarks>
    /// Age only, so the length never points at a hidden gem: 3 seasons at 21 or under, 2 from 22 to 29, 1 from 30.
    /// </remarks>
    /// <param name="age">The player's age in game years.</param>
    public static int FreeAgentContractSeasonsFor(int age)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);

        return age switch
        {
            <= 21 => 3,
            <= 29 => 2,
            _ => 1,
        };
    }

    /// <summary>The oldest age at which a signed free agent joins as a prospect rather than rotation (`FA-7`).</summary>
    public const int FreeAgentProspectAgeMax = 21;

    /// <summary>
    /// The share of the wages left on a contract that a club pays to release a player now, in basis points
    /// (`CON-12`). A balancing value.
    /// </summary>
    public const int ReleasePayoutBp = 5_000;
}

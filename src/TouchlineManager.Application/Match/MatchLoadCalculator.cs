using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Match;

/// <summary>One played fixture, as the match-load rule reads it.</summary>
/// <remarks>
/// The input is the frozen snapshot and the lines the engine produced from it, so the rule reads exactly the
/// facts the match was played from and cannot be moved by anything that happened after the lock (`MAT-1`).
/// </remarks>
/// <param name="FixtureId">The fixture that was played.</param>
/// <param name="HomeGoals">The host's goals.</param>
/// <param name="AwayGoals">The visitor's goals.</param>
/// <param name="Input">The frozen input the match was simulated from.</param>
/// <param name="PlayerLines">Every participant's line, in the engine's order.</param>
public sealed record MatchLoadInput(
    Guid FixtureId,
    int HomeGoals,
    int AwayGoals,
    MatchInputV1 Input,
    IReadOnlyList<MatchPlayerLineV1> PlayerLines);

/// <summary>What one played match left on one player (`TRN-11`, `TRN-13`).</summary>
/// <remarks>
/// Signed deltas rather than absolute values, because the value they are applied to is the player's own
/// current state: the daily progression job writes the same row, and a match that overwrote rather than
/// adjusted it would erase a training day.
/// </remarks>
/// <param name="PlayerId">The player, which is also the match participant's identity.</param>
/// <param name="ClubId">The club the player played for.</param>
/// <param name="ConditionDeltaBp">The condition the match consumed, zero or less.</param>
/// <param name="FatigueDeltaBp">The fatigue the match added, zero or more.</param>
/// <param name="MoraleDeltaBp">The morale the result and the player's minutes moved, either way.</param>
public sealed record MatchPlayerLoad(
    Guid PlayerId,
    Guid ClubId,
    int ConditionDeltaBp,
    int FatigueDeltaBp,
    int MoraleDeltaBp);

/// <summary>
/// Turns a matchday's results into the load they place on the players who played (`TRN-11`, `TRN-13`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function from the frozen facts of a match — each participant's minutes, their stamina, their
/// side's instructions, and the result — so a delayed publication re-derives the same load from the same
/// snapshot rather than from whatever the live tables now hold, and the load cannot drift from the result
/// that caused it. It reads no clock, database, culture, or global random source, so it produces no draw at
/// all: the rule is arithmetic, not chance.
/// </para>
/// <para>
/// <b>Condition</b> is consumed in proportion to minutes, and cost more for a player with less stamina and
/// for a side that plays harder. <b>Fatigue</b> accumulates on the same inputs but with its own scale,
/// because fatigue is the multi-match load while condition is the short-term freshness. Both are additive
/// costs that the daily progression job's recovery is measured against, so the two rules together decide
/// whether a squad can sustain a Tuesday-Thursday-Sunday season (`TRN-3`, `TRN-11`).
/// </para>
/// <para>
/// <b>Morale</b> moves with the result, scaled by how much the player contributed: a full match weighs more
/// than a late cameo, and a player who never left the bench takes no match-driven morale at all — the rule
/// names playing time as an input, so the absence of it is not an input (`TRN-13`). Contracts and transfers
/// are the other inputs the rule names; they belong to the stages that own them.
/// </para>
/// <para>
/// <b>Version.</b> <see cref="Version"/> is bumped whenever a coefficient or the intensity mapping changes,
/// because a change to either rewrites what a replay would produce (`MAT-9`, `TRN-9`). Nothing here is
/// stamped onto the world: like the training calculator, the rule versions itself, and the snapshot's own
/// facts are what a replay reads.
/// </para>
/// </remarks>
public static class MatchLoadCalculator
{
    /// <summary>The calculator version, bumped whenever a coefficient or the intensity mapping changes.</summary>
    public const string Version = "match-load-v1";

    /// <summary>The minutes a full regulation match is played for, which scales the morale contribution.</summary>
    public const int FullMatchMinutes = 90;

    /// <summary>Condition consumed per minute by a neutral player in a neutral side.</summary>
    /// <remarks>
    /// A balancing value, like the training coefficients it is measured against. A neutral ninety minutes
    /// costs a little under a thousand basis points of condition, which the daily progression's recovery
    /// roughly replaces between two matchdays — the property the calculator's tests pin, rather than a claim
    /// that any particular week is optimal.
    /// </remarks>
    public const int ConditionCostPerMinuteBp = 10;

    /// <summary>Fatigue accumulated per minute by a neutral player in a neutral side.</summary>
    /// <remarks>A balancing value, deliberately below the condition cost: fatigue is the slower measure.</remarks>
    public const int FatigueGainPerMinuteBp = 7;

    /// <summary>The stamina factor for a player with no stamina at all: the match costs them the most.</summary>
    public const int StaminaFactorLowBp = 12_000;

    /// <summary>The stamina factor for a player with maximum stamina: the match costs them the least.</summary>
    public const int StaminaFactorHighBp = 8_000;

    /// <summary>The morale a full match's win moves a player by.</summary>
    public const int MoraleWinBp = 350;

    /// <summary>The morale a full match's draw moves a player by.</summary>
    public const int MoraleDrawBp = 40;

    /// <summary>The morale a full match's defeat moves a player by, as a positive magnitude.</summary>
    public const int MoraleLossBp = 350;

    /// <summary>One whole of a basis-point measure: the divisor every factor here is scaled by.</summary>
    private const int BasisPointUnity = 10_000;

    /// <summary>The neutral intensity, which neither raises nor lowers a side's load.</summary>
    public const int NeutralIntensityBp = BasisPointUnity;

    /// <summary>The lowest an instruction set can scale a side's load.</summary>
    public const int MinIntensityBp = 6_000;

    /// <summary>The highest an instruction set can scale a side's load.</summary>
    public const int MaxIntensityBp = 16_000;

    /// <summary>Aggregates a matchday's results into one load per player who appeared.</summary>
    /// <param name="matches">The played fixtures of the round, in any order.</param>
    /// <returns>One load per player who played, in a stable order.</returns>
    public static IReadOnlyList<MatchPlayerLoad> Calculate(IEnumerable<MatchLoadInput> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var loads = new List<MatchPlayerLoad>();

        foreach (var match in matches)
        {
            ArgumentNullException.ThrowIfNull(match);

            var homeStamina = StaminaOf(match.Input.Home);
            var awayStamina = StaminaOf(match.Input.Away);
            var homeIntensity = IntensityBp(match.Input.Home.Instructions);
            var awayIntensity = IntensityBp(match.Input.Away.Instructions);

            foreach (var line in match.PlayerLines)
            {
                if (line.MinutesPlayed <= 0)
                {
                    continue;
                }

                var home = line.Side == MatchSide.Home;

                // A line names a participant the snapshot carried, because the engine produced the line from
                // that squad. A missing one is a corrupt document rather than a reason to guess a stamina.
                var stamina = (home ? homeStamina : awayStamina)[line.ParticipantId];
                var intensity = home ? homeIntensity : awayIntensity;

                loads.Add(new MatchPlayerLoad(
                    line.ParticipantId,
                    line.ClubId,
                    -ConditionCostBp(line.MinutesPlayed, stamina, intensity),
                    FatigueGainBp(line.MinutesPlayed, stamina, intensity),
                    MoraleDeltaBp(match, line)));
            }
        }

        return
        [
            .. loads
                .OrderBy(load => load.ClubId)
                .ThenBy(load => load.PlayerId),
        ];
    }

    /// <summary>Gets how much condition one player's minutes consumed.</summary>
    /// <param name="minutes">The minutes the player was on the pitch.</param>
    /// <param name="stamina">The player's frozen stamina, on the 1–20 scale.</param>
    /// <param name="intensityBp">The side's intensity, in basis points.</param>
    public static int ConditionCostBp(int minutes, int stamina, int intensityBp)
    {
        EnsureMinutes(minutes);
        EnsureStamina(stamina);
        EnsureIntensity(intensityBp);

        return Scaled(
            (long)minutes * ConditionCostPerMinuteBp,
            StaminaFactorBp(stamina),
            intensityBp);
    }

    /// <summary>Gets how much fatigue one player's minutes added.</summary>
    /// <param name="minutes">The minutes the player was on the pitch.</param>
    /// <param name="stamina">The player's frozen stamina, on the 1–20 scale.</param>
    /// <param name="intensityBp">The side's intensity, in basis points.</param>
    public static int FatigueGainBp(int minutes, int stamina, int intensityBp)
    {
        EnsureMinutes(minutes);
        EnsureStamina(stamina);
        EnsureIntensity(intensityBp);

        return Scaled(
            (long)minutes * FatigueGainPerMinuteBp,
            StaminaFactorBp(stamina),
            intensityBp);
    }

    /// <summary>
    /// Maps a side's eight instructions to one intensity, in basis points around the neutral set.
    /// </summary>
    /// <remarks>
    /// Every aggressive instruction raises the load and every conservative one lowers it, and the result is
    /// bounded, so no tactic multiplies a side's cost without a counter-cost (`INS-9`, master plan §3.8).
    /// Running the clock down is the one instruction that buys freshness, which is what makes it a choice
    /// rather than a free win.
    /// </remarks>
    /// <param name="instructions">The side's frozen instructions.</param>
    public static int IntensityBp(MatchInstructionsV1 instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var factor = (long)NeutralIntensityBp;

        factor = factor * (instructions.Mentality switch
        {
            MatchMentality.Defensive => 9_000,
            MatchMentality.Cautious => 9_500,
            MatchMentality.Positive => 10_800,
            MatchMentality.Attacking => 11_500,
            _ => NeutralIntensityBp,
        }) / BasisPointUnity;

        factor = factor * (instructions.Tempo switch
        {
            MatchTempo.Low => 9_200,
            MatchTempo.High => 11_000,
            _ => NeutralIntensityBp,
        }) / BasisPointUnity;

        factor = factor * (instructions.Pressing switch
        {
            MatchPressing.LowBlock => 9_000,
            MatchPressing.HighPress => 11_500,
            _ => NeutralIntensityBp,
        }) / BasisPointUnity;

        factor = factor * (instructions.DefensiveLine switch
        {
            MatchDefensiveLine.Deep => 9_500,
            MatchDefensiveLine.High => 10_500,
            _ => NeutralIntensityBp,
        }) / BasisPointUnity;

        factor = factor * (instructions.TimeWasting switch
        {
            MatchTimeWasting.Situational => 9_700,
            MatchTimeWasting.On => 9_400,
            _ => NeutralIntensityBp,
        }) / BasisPointUnity;

        return (int)Math.Clamp(factor, MinIntensityBp, MaxIntensityBp);
    }

    /// <summary>Gets the morale movement a result and a player's minutes produced.</summary>
    /// <param name="match">The played fixture.</param>
    /// <param name="line">The player's line.</param>
    public static int MoraleDeltaBp(MatchLoadInput match, MatchPlayerLineV1 line)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(line);
        EnsureMinutes(line.MinutesPlayed);

        var won = line.Side == MatchSide.Home
            ? match.HomeGoals > match.AwayGoals
            : match.AwayGoals > match.HomeGoals;
        var lost = line.Side == MatchSide.Home
            ? match.HomeGoals < match.AwayGoals
            : match.AwayGoals < match.HomeGoals;

        var resultBp = won ? MoraleWinBp : lost ? -MoraleLossBp : MoraleDrawBp;

        // Playing time is the weight: a full match carries the result whole, a cameo carries it in
        // proportion, and nobody who stayed on the bench carries any of it (TRN-13).
        var weightBp = Math.Min(BasisPointUnity, line.MinutesPlayed * BasisPointUnity / FullMatchMinutes);

        return resultBp * weightBp / BasisPointUnity;
    }

    /// <summary>Reads every participant's stamina, keyed by participant identity.</summary>
    private static Dictionary<Guid, int> StaminaOf(MatchSideV1 side)
    {
        var stamina = new Dictionary<Guid, int>(side.Squad.Count);

        foreach (var participant in side.Squad)
        {
            stamina[participant.ParticipantId] = participant.Attributes.ValueOf(MatchAttributeName.Stamina);
        }

        return stamina;
    }

    /// <summary>Scales a base cost by a stamina factor and an intensity factor, as exact integers.</summary>
    private static int Scaled(long baseBp, int staminaFactorBp, int intensityBp)
    {
        var scaled = baseBp * staminaFactorBp / BasisPointUnity;

        return (int)(scaled * intensityBp / BasisPointUnity);
    }

    /// <summary>The load multiplier a stamina value implies: fitter players pay less.</summary>
    private static int StaminaFactorBp(int stamina) =>
        StaminaFactorLowBp
        - (int)((long)(stamina - MatchAttributeNames.Min) * (StaminaFactorLowBp - StaminaFactorHighBp)
            / (MatchAttributeNames.Max - MatchAttributeNames.Min));

    private static void EnsureMinutes(int minutes)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Minutes played are never negative.");
        }
    }

    private static void EnsureStamina(int stamina)
    {
        if (stamina is < MatchAttributeNames.Min or > MatchAttributeNames.Max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stamina),
                stamina,
                $"Stamina is between {MatchAttributeNames.Min} and {MatchAttributeNames.Max} (TRN-4).");
        }
    }

    private static void EnsureIntensity(int intensityBp)
    {
        if (intensityBp is < MinIntensityBp or > MaxIntensityBp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intensityBp),
                intensityBp,
                $"An intensity is between {MinIntensityBp} and {MaxIntensityBp} basis points.");
        }
    }
}

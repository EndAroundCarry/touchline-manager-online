using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Squad;

/// <summary>How an AI club reacts to the game: when it protects a lead, when it chases one, and how far it moves (`MAT-6`, `INS-12`).</summary>
public enum AiTemperament
{
    /// <summary>Protects a lead early, chases late, and steps far towards defence.</summary>
    Cautious = 0,

    /// <summary>The middle of the three.</summary>
    Balanced = 1,

    /// <summary>Protects a lead late, chases early, and steps far towards attack.</summary>
    Bold = 2,
}

/// <summary>What the AI decides for a club's match instructions: its temperament and the instructions that follow from it.</summary>
/// <param name="Temperament">The club's temperament.</param>
/// <param name="Instructions">The club's match instructions, in the order they are checked. Each is one a manager could write.</param>
public sealed record AiMatchPlan(AiTemperament Temperament, IReadOnlyList<MatchInstructionDefinition> Instructions);

/// <summary>
/// The deterministic match plan an AI club plays by (`MAT-6`, `INS-12`).
/// </summary>
/// <remarks>
/// <para>
/// A pure, versioned function of the club's identity, its shape and its instructions. It is seeded from its own label
/// (<see cref="AiPolicyVersions.MatchPlanVersion"/>) and draws from its own generator, never from <see cref="AiClubPolicy"/>'s, so adding the plan
/// moved no choice a club had already been given: the shape, the instructions and the eleven are the same as they were.
/// </para>
/// <para>
/// Every instruction it writes is one a manager could write on the Match instructions tab, and the plan goes through the same
/// <see cref="MatchInstructionValidator"/> a human's save does (`INS-12`): the AI receives no bypass, only a different author. It names no player,
/// only families ("the most tired midfielder", "a fresh forward"), because an AI club's bench is picked by the snapshot builder on the day.
/// </para>
/// <para>
/// The five instructions are the ones any assistant would give: protect a lead of two or more, hold a one-goal lead late with a fresher player in
/// the place of the most tired attacker, chase the game when behind with a fresh forward for the most tired midfielder, go all out late when still
/// behind, and settle down when a player has been sent off. The club's temperament moves their minutes and the size of the steps.
/// </para>
/// </remarks>
public static class AiMatchPlanPolicy
{
    /// <summary>The goal margin the protect instruction waits for: a lead of two or more.</summary>
    public const int ProtectMargin = 2;

    /// <summary>The goal margin the hold instruction waits for: a one-goal lead.</summary>
    public const int HoldMargin = 1;

    /// <summary>The goal margin the chase instructions wait for: behind by one or more.</summary>
    public const int ChaseMargin = 1;

    /// <summary>The timings and step sizes of one temperament.</summary>
    /// <param name="ProtectFrom">The minute protecting a lead of two or more begins.</param>
    /// <param name="HoldFrom">The minute holding a one-goal lead begins.</param>
    /// <param name="ChaseFrom">The minute chasing the game begins.</param>
    /// <param name="AllOutFrom">The minute going all out begins.</param>
    /// <param name="DefensiveSteps">How many steps towards defence protecting a lead, and a sending-off, move the mentality.</param>
    /// <param name="ChaseSteps">How many steps towards attack chasing the game moves the mentality.</param>
    /// <param name="HoldReplacement">The family whose fresh player takes the most tired attacker's place while a one-goal lead is held.</param>
    private sealed record Profile(
        int ProtectFrom,
        int HoldFrom,
        int ChaseFrom,
        int AllOutFrom,
        int DefensiveSteps,
        int ChaseSteps,
        PositionFamily HoldReplacement);

    /// <summary>Cautious: protects from the 65th minute, chases from the 70th, and steps two places towards defence.</summary>
    private static readonly Profile CautiousProfile = new(65, 75, 70, 88, 2, 1, PositionFamily.Defence);

    /// <summary>Balanced: protects from the 70th minute and chases from the 65th.</summary>
    private static readonly Profile BalancedProfile = new(70, 80, 65, 85, 1, 1, PositionFamily.Midfield);

    /// <summary>Bold: protects from the 75th minute, chases from the 60th, and steps two places towards attack.</summary>
    private static readonly Profile BoldProfile = new(75, 85, 60, 82, 1, 2, PositionFamily.Midfield);

    /// <summary>Decides a club's match instructions.</summary>
    /// <param name="clubId">The club, which is the seed's identity.</param>
    /// <param name="formation">The shape the club starts from, which decides how it settles down when a player is sent off.</param>
    /// <param name="instructions">The team instructions the club starts from, so no instruction ever loosens one the club already holds tighter.</param>
    public static AiMatchPlan Decide(Guid clubId, FormationPreset formation, TeamInstructionSet instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var draws = new Pcg32(DeterministicDigest.SeedOf(AiPolicyVersions.MatchPlanVersion, clubId.ToString("D")));
        var temperament = Enum.GetValues<AiTemperament>()[draws.NextInt(Enum.GetValues<AiTemperament>().Length)];

        var profile = temperament switch
        {
            AiTemperament.Cautious => CautiousProfile,
            AiTemperament.Bold => BoldProfile,
            _ => BalancedProfile,
        };

        return new AiMatchPlan(
            temperament,
            [
                Protect(profile, instructions),
                Hold(profile),
                Chase(profile),
                AllOut(profile),
                TenMen(profile, formation),
            ]);
    }

    /// <summary>
    /// The shape a side settles into when it is a player short: one forward fewer and one more midfielder, the back line as it was, or null
    /// when the side already plays a single forward.
    /// </summary>
    /// <param name="formation">The shape the side starts from.</param>
    public static FormationPreset? SettledShape(FormationPreset formation) => formation switch
    {
        FormationPreset.FourFourTwo or FormationPreset.ThreeFiveTwo => FormationPreset.FourFiveOne,
        FormationPreset.FourThreeThree => FormationPreset.FourFourTwo,
        FormationPreset.FiveThreeTwo => FormationPreset.FiveFourOne,
        _ => null,
    };

    private static MatchInstructionDefinition Protect(Profile profile, TeamInstructionSet instructions) => new()
    {
        Name = "Protect a two-goal lead",
        FromMinute = profile.ProtectFrom,
        Score = MatchInstructionScore.Leading,
        ScoreMargin = ProtectMargin,
        Changes = new MatchInstructionChanges
        {
            MentalitySteps = -profile.DefensiveSteps,

            // Time-wasting is only ever raised: a club that already wastes time as much as it may keeps doing so.
            TimeWasting = instructions.TimeWasting == TimeWasting.Off ? TimeWasting.Situational : null,
        },
    };

    private static MatchInstructionDefinition Hold(Profile profile) => new()
    {
        Name = "Hold a narrow lead",
        FromMinute = profile.HoldFrom,
        Score = MatchInstructionScore.Leading,
        ScoreMargin = HoldMargin,
        Changes = new MatchInstructionChanges { MentalitySteps = -1 },
        Substitutions =
        [
            new MatchInstructionSubstitution(
                MatchInstructionOff.MostTiredInFamily,
                null,
                PositionFamily.Attack,
                MatchInstructionOn.BestFitInFamily,
                null,
                profile.HoldReplacement),
        ],
    };

    private static MatchInstructionDefinition Chase(Profile profile) => new()
    {
        Name = "Chase the game",
        FromMinute = profile.ChaseFrom,
        Score = MatchInstructionScore.Trailing,
        ScoreMargin = ChaseMargin,
        Changes = new MatchInstructionChanges { MentalitySteps = profile.ChaseSteps },
        Substitutions =
        [
            new MatchInstructionSubstitution(
                MatchInstructionOff.MostTiredInFamily,
                null,
                PositionFamily.Midfield,
                MatchInstructionOn.BestFitInFamily,
                null,
                PositionFamily.Attack),
        ],
    };

    private static MatchInstructionDefinition AllOut(Profile profile) => new()
    {
        Name = "Go all out",
        FromMinute = profile.AllOutFrom,
        Score = MatchInstructionScore.Trailing,
        ScoreMargin = ChaseMargin,
        Changes = new MatchInstructionChanges { Mentality = Mentality.Attacking, Pressing = Pressing.HighPress },
    };

    private static MatchInstructionDefinition TenMen(Profile profile, FormationPreset formation) => new()
    {
        Name = "Settle down with ten men",
        Event = MatchInstructionEvent.OwnSentOff,
        Formation = SettledShape(formation),
        Changes = new MatchInstructionChanges { MentalitySteps = -profile.DefensiveSteps },
    };
}

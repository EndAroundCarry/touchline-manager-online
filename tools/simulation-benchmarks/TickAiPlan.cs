using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// Gives a laboratory side the match plan the AI would write for it (plan i-want-you-to-curious-globe, Milestone 6), and a bench like a real
/// club's, so the plan's substitutions have someone to bring on.
/// </summary>
/// <remarks>
/// The plan is the real one: <see cref="AiMatchPlanPolicy"/> decides it from a club identity made from the match seed (so the mix of cautious,
/// balanced and bold clubs is the league's, a different club each match) and <see cref="MatchSnapshotBuilder.PlanOf"/> turns it into the
/// engine's plan exactly as a fixture's snapshot does. A lab side keeps neutral instructions as far as the policy is concerned (time-wasting off).
/// </remarks>
internal static class TickAiPlan
{
    /// <summary>Which sides carry the AI plan: from the environment variable <c>TP_AI</c> (<c>both</c>, <c>home</c>, <c>away</c>, or nothing).</summary>
    public static string Sides => (Environment.GetEnvironmentVariable("TP_AI") ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Whether the bench is a real club's: from <c>TP_BENCH=real</c>. The lab's own is a keeper and central midfielders.</summary>
    public static bool RealBench => string.Equals(Environment.GetEnvironmentVariable("TP_BENCH"), "real", StringComparison.OrdinalIgnoreCase);

    /// <summary>The suffix a variant adds to the probe's file names, so a run never overwrites another's.</summary>
    public static string Tag => (RealBench ? "-realbench" : string.Empty) + (Sides.Length == 0 ? string.Empty : "-ai-" + Sides);

    /// <summary>A bench like a real club's: a keeper, a centre back, a full back, two midfielders, a winger and a striker, in that order.</summary>
    private static readonly MatchPosition[] BenchPositions =
    [
        MatchPosition.Goalkeeper,
        MatchPosition.CentreBack,
        MatchPosition.RightBack,
        MatchPosition.CentralMidfielder,
        MatchPosition.AttackingMidfielder,
        MatchPosition.RightWinger,
        MatchPosition.Striker,
    ];

    /// <summary>Applies the variant to a match.</summary>
    /// <param name="input">The match, with the formations stood.</param>
    /// <param name="seed">The match seed.</param>
    /// <param name="homeShape">The home side's formation.</param>
    /// <param name="awayShape">The away side's formation.</param>
    public static MatchInputV1 Apply(MatchInputV1 input, ulong seed, FormationPreset homeShape, FormationPreset awayShape)
    {
        if (RealBench)
        {
            input = input with { Home = WithBench(input.Home), Away = WithBench(input.Away) };
        }

        var sides = Sides;

        if (sides is "both" or "home")
        {
            input = input with { Home = input.Home with { MatchPlan = PlanFor(seed, 0, homeShape) } };
        }

        if (sides is "both" or "away")
        {
            input = input with { Away = input.Away with { MatchPlan = PlanFor(seed, 1, awayShape) } };
        }

        return input;
    }

    /// <summary>The plan the AI writes for the club that plays one side of a match.</summary>
    /// <param name="seed">The match seed.</param>
    /// <param name="side">0 for home, 1 for away.</param>
    /// <param name="shape">The side's formation.</param>
    public static MatchPlanV1 PlanFor(ulong seed, int side, FormationPreset shape)
    {
        var plan = AiMatchPlanPolicy.Decide(ClubOf(seed, side), shape, TeamInstructionSet.Neutral);

        // Ablations for tuning: TP_AI_DROP is a comma list of rule places (1-5) to leave out, TP_AI_NOSUB a list whose substitutions are removed.
        var dropped = Places("TP_AI_DROP");
        var withoutSubstitutions = Places("TP_AI_NOSUB");
        var instructions = plan.Instructions
            .Select((instruction, index) => (instruction, place: index + 1))
            .Where(entry => !dropped.Contains(entry.place))
            .Select(entry => withoutSubstitutions.Contains(entry.place) ? entry.instruction with { Substitutions = [] } : entry.instruction)
            .ToList();

        return MatchSnapshotBuilder.PlanOf(instructions, assistantFatigueSubstitutions: true, []);
    }

    private static HashSet<int> Places(string variable) =>
        [.. (Environment.GetEnvironmentVariable(variable) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)];

    /// <summary>The temperament of the club that plays one side of a match.</summary>
    public static AiTemperament TemperamentOf(ulong seed, int side, FormationPreset shape) =>
        AiMatchPlanPolicy.Decide(ClubOf(seed, side), shape, TeamInstructionSet.Neutral).Temperament;

    private static Guid ClubOf(ulong seed, int side) =>
        new((int)(seed & 0x7fffffff), (short)((seed >> 31) & 0x7fff), (short)side, 1, 2, 3, 4, 5, 6, 7, 8);

    private static MatchSideV1 WithBench(MatchSideV1 side)
    {
        var starters = side.Slots.Select(slot => slot.ParticipantId).ToHashSet();
        var bench = 0;

        return side with
        {
            Squad =
            [
                .. side.Squad.Select(participant => starters.Contains(participant.ParticipantId) || bench >= BenchPositions.Length
                    ? participant
                    : participant with { Position = BenchPositions[bench++] }),
            ],
        };
    }
}

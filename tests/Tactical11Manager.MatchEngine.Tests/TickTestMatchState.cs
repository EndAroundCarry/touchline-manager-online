using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Randomness;
using Tactical11Manager.MatchEngine.Ratings;
using Tactical11Manager.MatchEngine.Simulation;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// Builds a live <see cref="MatchState"/> for the tick engine's tests that must put events on the log.
/// </summary>
internal static class TickTestMatchState
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    /// <summary>Builds a state for two evenly matched sides.</summary>
    /// <param name="seed">The match seed.</param>
    /// <param name="homeAbility">The home side's uniform attribute value.</param>
    /// <param name="awayAbility">The away side's uniform attribute value.</param>
    public static MatchState Create(ulong seed = 20_260_925, int homeAbility = 13, int awayAbility = 13) =>
        Create(TestMatchFactory.Even(seed, homeAbility, awayAbility));

    /// <summary>Builds a state for a snapshot.</summary>
    /// <param name="input">The snapshot.</param>
    /// <param name="passages">The recorder to capture a film into, or null.</param>
    public static MatchState Create(MatchInputV1 input, MatchPassageRecorder? passages = null)
    {
        var home = BuildSide(input.Home, MatchSide.Home);
        var away = BuildSide(input.Away, MatchSide.Away);

        return new MatchState(input, Rules, new Pcg32(input.Seed), home, away) { Passages = passages };
    }

    private static SideRuntime BuildSide(MatchSideV1 side, MatchSide which)
    {
        var lineup = LineupResolver.Resolve(side, which, Rules);
        var runtime = new SideRuntime { Which = which, Lineup = lineup, Bench = [.. lineup.Bench] };

        foreach (var slot in lineup.Slots)
        {
            runtime.Active.Add(ActiveSlot.From(slot));
        }

        runtime.RecalculateRatings(Rules);

        return runtime;
    }
}

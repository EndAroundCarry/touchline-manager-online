using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Builds a live <see cref="MatchState"/> for the tick engine's tests that must put events on the log.
/// </summary>
internal static class TickTestMatchState
{
    private static readonly EngineRulesV2 Rules = EngineRulesV2.Default;

    /// <summary>Builds a state for two evenly matched sides.</summary>
    /// <param name="seed">The match seed.</param>
    public static MatchState Create(ulong seed = 20_260_925)
    {
        var input = TestMatchFactory.Even(seed);
        var home = BuildSide(input.Home, MatchSide.Home);
        var away = BuildSide(input.Away, MatchSide.Away);

        return new MatchState(input, Rules, new Pcg32(input.Seed), home, away);
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

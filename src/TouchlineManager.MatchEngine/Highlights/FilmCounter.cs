using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// How big a counter-attack looks in the film (`replay-v6`): how long into the possession the forwards stay up the
/// pitch as outlets, how far, how many of them and of the midfield run, and whether the other side drops back
/// instead of chasing.
/// </summary>
/// <remarks>
/// <para>
/// The engine decides which possessions are counters (`engine-v11`): one in five of the balls a side wins back, and
/// one in two when the side has asked to play on the counter. The film draws exactly those, at the size its side's
/// tactic gives them: a small one by default and a bigger one when the instruction is on. All the numbers are in the
/// two rows below.
/// </para>
/// <para>
/// This is how the film draws the move; it changes nothing the simulation decided.
/// </para>
/// </remarks>
/// <param name="BreakBeats">How many beats into the possession the counter's shape holds.</param>
/// <param name="OutletAhead">How far in front of the ball the forwards hold, in metres.</param>
/// <param name="RunnerAhead">How far in front of the ball the midfield runs, in metres.</param>
/// <param name="Outlets">How many forwards hold high as outlets: one or two.</param>
/// <param name="Runners">How many midfielders run the lanes behind them: one or two.</param>
/// <param name="DefendersDrop">Whether the other side drops back and closes up instead of sending a challenger.</param>
internal sealed record CounterTuning(
    int BreakBeats,
    double OutletAhead,
    double RunnerAhead,
    int Outlets,
    int Runners,
    bool DefendersDrop)
{
    /// <summary>The side has not asked to play on the counter: one outlet, one runner, and the defenders press as usual.</summary>
    public static CounterTuning Base { get; } = new(3, 24.0, 10.0, 1, 1, false);

    /// <summary>The side plays on the counter: two outlets high up the pitch, two runners, and the defenders drop.</summary>
    public static CounterTuning On { get; } = new(6, 40.0, 18.0, 2, 2, true);

    /// <summary>Gets the tuning a side's instructions ask for.</summary>
    /// <param name="instructions">The side's instructions.</param>
    public static CounterTuning For(MatchInstructionsV1 instructions) => instructions.CounterAttack ? On : Base;
}

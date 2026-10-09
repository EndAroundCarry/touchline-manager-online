using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>What the match is doing: open play, one of the dead-ball restarts waiting to be taken, or a break.</summary>
internal enum TickPlayState
{
    /// <summary>The ball is in play.</summary>
    OpenPlay = 0,

    /// <summary>A kick-off is being set up at the centre spot.</summary>
    KickOffPending = 1,

    /// <summary>A goal kick is being set up in the six-yard box.</summary>
    GoalKickPending = 2,

    /// <summary>A corner is being set up at the corner flag.</summary>
    CornerPending = 3,

    /// <summary>A throw-in is being set up on the touchline.</summary>
    ThrowInPending = 4,

    /// <summary>A free kick is being set up where the foul was.</summary>
    FreeKickPending = 5,

    /// <summary>A penalty is being set up on the spot.</summary>
    PenaltyPending = 6,

    /// <summary>A goal has been scored and the players are celebrating; the conceding side kicks off next.</summary>
    GoalCelebration = 7,

    /// <summary>The first half is over. Nothing is played until the loop starts the second half's kick-off.</summary>
    HalfTime = 8,
}

/// <summary>Which dead-ball restart is being taken.</summary>
internal enum TickRestartKind
{
    /// <summary>The ball is kicked off from the centre spot.</summary>
    KickOff = 0,

    /// <summary>The goalkeeper restarts play from the six-yard box.</summary>
    GoalKick = 1,

    /// <summary>The attacking side takes a corner.</summary>
    Corner = 2,

    /// <summary>The ball is thrown in from the touchline.</summary>
    ThrowIn = 3,

    /// <summary>A free kick, direct or indirect.</summary>
    FreeKick = 4,

    /// <summary>A penalty kick.</summary>
    Penalty = 5,
}

/// <summary>
/// One restart: which kind, which side takes it and where the ball is put, in absolute pitch units.
/// </summary>
/// <param name="Kind">The kind of restart.</param>
/// <param name="TakerIsHome">Whether the home side takes it (and attacks towards high X).</param>
/// <param name="SpotX">The ball's X, in pitch units.</param>
/// <param name="SpotY">The ball's Y, in pitch units.</param>
internal readonly record struct TickRestart(TickRestartKind Kind, bool TakerIsHome, int SpotX, int SpotY)
{
    /// <summary>The depth of a goal kick from the goal line, in pitch units (4.7 m, inside the 5.5 m six-yard box).</summary>
    public const int GoalKickDepth = 450;

    /// <summary>The widest a goal kick is placed from the middle, in pitch units (the six-yard box is 18.3 m wide).</summary>
    public const int GoalKickHalfWidth = 800;

    /// <summary>How far in from both lines the ball is put at a corner, in pitch units.</summary>
    public const int CornerInset = 30;

    /// <summary>The closest to a line a free kick is placed, in pitch units.</summary>
    public const int FreeKickInset = 100;

    /// <summary>The home side's penalty spot is 11 m from the goal it attacks, in pitch units.</summary>
    public const int PenaltySpotDepth = 1_100;

    /// <summary>Gets the restart for the centre spot.</summary>
    /// <param name="homeTakes">Whether the home side kicks off.</param>
    public static TickRestart KickOff(bool homeTakes) =>
        new(TickRestartKind.KickOff, homeTakes, SpatialPitch.PitchLength / 2, SpatialPitch.GoalYCenter);

    /// <summary>Gets the goal kick for the side whose goal the ball crossed.</summary>
    /// <param name="homeTakes">Whether the home goalkeeper takes it (the ball crossed the home goal line).</param>
    /// <param name="ballY">Where the ball crossed the goal line; the kick is taken from the same side of the box.</param>
    public static TickRestart GoalKick(bool homeTakes, int ballY) =>
        new(
            TickRestartKind.GoalKick,
            homeTakes,
            homeTakes ? GoalKickDepth : SpatialPitch.PitchLength - GoalKickDepth,
            Math.Clamp(ballY, SpatialPitch.GoalYCenter - GoalKickHalfWidth, SpatialPitch.GoalYCenter + GoalKickHalfWidth));

    /// <summary>Gets the corner for the attacking side, taken from the flag nearest where the ball left the pitch.</summary>
    /// <param name="homeTakes">Whether the home side takes it (it attacks the high-X goal line).</param>
    /// <param name="ballY">Where the ball crossed the goal line.</param>
    public static TickRestart Corner(bool homeTakes, int ballY) =>
        new(
            TickRestartKind.Corner,
            homeTakes,
            homeTakes ? SpatialPitch.PitchLength - CornerInset : CornerInset,
            ballY < SpatialPitch.GoalYCenter ? CornerInset : SpatialPitch.PitchWidth - CornerInset);

    /// <summary>Gets the throw-in from the touchline the ball crossed.</summary>
    /// <param name="homeTakes">Whether the home side throws.</param>
    /// <param name="ballX">Where the ball crossed the touchline.</param>
    /// <param name="ballY">Which touchline: the nearer one to this Y.</param>
    public static TickRestart ThrowIn(bool homeTakes, int ballX, int ballY) =>
        new(
            TickRestartKind.ThrowIn,
            homeTakes,
            Math.Clamp(ballX, FreeKickInset, SpatialPitch.PitchLength - FreeKickInset),
            ballY < SpatialPitch.GoalYCenter ? 0 : SpatialPitch.PitchWidth);

    /// <summary>Gets the free kick where the foul was.</summary>
    /// <param name="homeTakes">Whether the home side takes it.</param>
    /// <param name="x">The foul's X.</param>
    /// <param name="y">The foul's Y.</param>
    public static TickRestart FreeKick(bool homeTakes, int x, int y) =>
        new(
            TickRestartKind.FreeKick,
            homeTakes,
            Math.Clamp(x, FreeKickInset, SpatialPitch.PitchLength - FreeKickInset),
            Math.Clamp(y, FreeKickInset, SpatialPitch.PitchWidth - FreeKickInset));

    /// <summary>Gets the penalty at the end the taker attacks.</summary>
    /// <param name="homeTakes">Whether the home side takes it (at the high-X end).</param>
    public static TickRestart Penalty(bool homeTakes) =>
        new(
            TickRestartKind.Penalty,
            homeTakes,
            homeTakes ? SpatialPitch.PitchLength - PenaltySpotDepth : PenaltySpotDepth,
            SpatialPitch.GoalYCenter);
}

/// <summary>What the loop is to do this tick about the restart.</summary>
internal enum TickRestartSignal
{
    /// <summary>Open play: nothing to do about restarts.</summary>
    None = 0,

    /// <summary>
    /// The machine has just moved into a new restart on its own (a celebration ended and the kick-off is due): put the ball
    /// on the <see cref="TickMatchStateMachine.Restart"/> spot and start placing the players.
    /// </summary>
    SetUp = 1,

    /// <summary>A restart is being set up: keep placing the players and keep the ball where it lies.</summary>
    Waiting = 2,

    /// <summary>The set-up is over and the taker strikes the ball this tick. The machine is back in open play.</summary>
    Execute = 3,

    /// <summary>A goal is being celebrated: the clock-and-ball stay frozen and the scorers run off.</summary>
    Celebrating = 4,

    /// <summary>The half is over.</summary>
    Break = 5,
}

/// <summary>
/// The set-piece state machine of the tick engine: who has the ball dead and how long the whistle holds it
/// (`tick-engine-v1`, Milestone 7).
/// </summary>
/// <remarks>
/// <para>
/// The machine owns <em>time</em> only; where the 22 players stand and what the taker does are
/// <see cref="TickSetPieces"/>. The loop calls <see cref="Begin"/> when play stops, then <see cref="Step"/> every tick: the
/// machine answers <see cref="TickRestartSignal.Waiting"/> while the sides take up their shapes and
/// <see cref="TickRestartSignal.Execute"/> on the tick the taker may strike.
/// </para>
/// <para>
/// <b>The hold.</b> Every restart is held for at least its setup time so that the shapes can form: 0.8 s for a throw-in, 1.0 s
/// for a goal kick, 1.2 s for a kick-off, 1.5 s for a corner, a free kick or a penalty. After that it waits for the taker to
/// be standing at the ball, and gives up waiting after 10 s (a taker who cannot get there must not stall the match).
/// </para>
/// <para>
/// <b>After a goal</b> (<see cref="BeginGoalCelebration"/>) the machine holds for 8 s, then starts the conceding side's
/// kick-off by itself and answers <see cref="TickRestartSignal.SetUp"/> once. <b>Half-time</b>
/// (<see cref="BeginHalfTime"/>) holds until the loop begins the second half's kick-off.
/// </para>
/// <para>
/// Offside (`LAW 11`) cannot be given from a goal kick, a throw-in or a corner, and the first ball of a
/// penalty or a kick-off is never judged either: <see cref="OffsideApplies"/> is the one place that rule lives. The machine is a small object
/// created once per match; stepping it allocates nothing.
/// </para>
/// </remarks>
internal sealed class TickMatchStateMachine
{
    /// <summary>The ticks a goal is celebrated before the kick-off is set up (8 s).</summary>
    public const int CelebrationTicks = 80;

    /// <summary>The ticks after which a restart is taken whether or not the taker has reached the ball (10 s).</summary>
    public const int MaximumSetupTicks = 100;

    private bool _scorerIsHome;

    /// <summary>Gets what the match is doing.</summary>
    public TickPlayState State { get; private set; } = TickPlayState.OpenPlay;

    /// <summary>
    /// Gets the restart being set up, or, once it has been executed, the one just taken (the loop reads it to hand the taker his kick).
    /// </summary>
    public TickRestart Restart { get; private set; }

    /// <summary>Gets the ticks spent in the current state.</summary>
    public int TicksInState { get; private set; }

    /// <summary>Gets a value indicating whether the ball is dead: a restart is pending, a goal is being celebrated or it is half-time.</summary>
    public bool IsDeadBall => State != TickPlayState.OpenPlay;

    /// <summary>Gets the minimum hold of a restart, in ticks (0.8 s to 1.5 s).</summary>
    /// <param name="kind">The kind of restart.</param>
    public static int SetupTicks(TickRestartKind kind) => kind switch
    {
        TickRestartKind.ThrowIn => 8,
        TickRestartKind.GoalKick => 10,
        TickRestartKind.KickOff => 12,
        _ => 15,
    };

    /// <summary>Gets the pending state a kind of restart puts the machine in.</summary>
    /// <param name="kind">The kind of restart.</param>
    public static TickPlayState PendingStateOf(TickRestartKind kind) => kind switch
    {
        TickRestartKind.KickOff => TickPlayState.KickOffPending,
        TickRestartKind.GoalKick => TickPlayState.GoalKickPending,
        TickRestartKind.Corner => TickPlayState.CornerPending,
        TickRestartKind.ThrowIn => TickPlayState.ThrowInPending,
        TickRestartKind.FreeKick => TickPlayState.FreeKickPending,
        _ => TickPlayState.PenaltyPending,
    };

    /// <summary>
    /// Tells whether a ball played from a restart can leave a teammate offside: not from a goal kick, a throw-in or a corner.
    /// </summary>
    /// <param name="kind">The kind of restart.</param>
    public static bool OffsideApplies(TickRestartKind kind) =>
        kind is TickRestartKind.FreeKick;

    /// <summary>Starts a restart: the ball is dead until the taker strikes it.</summary>
    /// <param name="restart">The restart.</param>
    public void Begin(in TickRestart restart)
    {
        Restart = restart;
        State = PendingStateOf(restart.Kind);
        TicksInState = 0;
    }

    /// <summary>Starts the celebration of a goal. The conceding side kicks off when it ends.</summary>
    /// <param name="scoredByHome">Whether the home side scored.</param>
    public void BeginGoalCelebration(bool scoredByHome)
    {
        _scorerIsHome = scoredByHome;
        State = TickPlayState.GoalCelebration;
        TicksInState = 0;
    }

    /// <summary>Ends the half. The machine stays there until <see cref="Begin"/> starts the second half.</summary>
    public void BeginHalfTime()
    {
        State = TickPlayState.HalfTime;
        TicksInState = 0;
    }

    /// <summary>Advances the machine one tick.</summary>
    /// <param name="takerReady">Whether the taker is standing at the ball (see <see cref="TickSetPieces.IsTakerReady"/>).</param>
    /// <returns>What the loop does about the restart this tick.</returns>
    public TickRestartSignal Step(bool takerReady)
    {
        switch (State)
        {
            case TickPlayState.OpenPlay:
                return TickRestartSignal.None;

            case TickPlayState.HalfTime:
                return TickRestartSignal.Break;

            case TickPlayState.GoalCelebration:
                TicksInState++;

                if (TicksInState < CelebrationTicks)
                {
                    return TickRestartSignal.Celebrating;
                }

                Begin(TickRestart.KickOff(homeTakes: !_scorerIsHome));

                return TickRestartSignal.SetUp;

            default:
                TicksInState++;

                if (TicksInState >= MaximumSetupTicks || (takerReady && TicksInState >= SetupTicks(Restart.Kind)))
                {
                    State = TickPlayState.OpenPlay;
                    TicksInState = 0;

                    return TickRestartSignal.Execute;
                }

                return TickRestartSignal.Waiting;
        }
    }
}

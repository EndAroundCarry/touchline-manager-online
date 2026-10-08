using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>Where a match stands between live play and a dead ball (`tick-engine-v1`, Milestone 7).</summary>
/// <remarks>
/// <para>
/// Every phase but <see cref="OpenPlay"/> is a dead ball. The players keep moving and the steered bodies keep running, but
/// the ball is out of play until the restart is taken; the phase tells the tick loop which set-piece picture to steer the
/// 22 players into and which kick to expect when the setup hold ends.
/// </para>
/// <para>
/// The pending phases double as the restart's kind, which is what the setup in <see cref="TickSetPieces"/> switches on.
/// </para>
/// </remarks>
internal enum TickMatchPhase
{
    /// <summary>Live play: the ball is in play and the tick simulation runs.</summary>
    OpenPlay = 0,

    /// <summary>A kick-off is being set up.</summary>
    KickOffPending = 1,

    /// <summary>A goal kick is being set up.</summary>
    GoalKickPending = 2,

    /// <summary>A corner is being set up.</summary>
    CornerPending = 3,

    /// <summary>A throw-in is being set up.</summary>
    ThrowInPending = 4,

    /// <summary>A free kick is being set up.</summary>
    FreeKickPending = 5,

    /// <summary>A penalty is being set up.</summary>
    PenaltyPending = 6,

    /// <summary>A goal has been scored and the scoring side is celebrating.</summary>
    GoalCelebration = 7,

    /// <summary>The first half has ended.</summary>
    HalfTime = 8,
}

/// <summary>What the restarts of a match have in common: who takes it, from where, and from which phase.</summary>
/// <remarks>
/// The record is the fact the tick loop steers from and, later, the replay shows: the ball is placed on
/// <see cref="Ball"/> while the phase holds, and the side that takes the restart is <see cref="Side"/>. A
/// <see cref="TickMatchPhase.GoalCelebration"/> carries the side that will kick off once the celebration is over, so the
/// chain goal to kick-off needs no second record.
/// </remarks>
/// <param name="Kind">The phase the restart is taken from (always a pending phase or a celebration).</param>
/// <param name="Side">The side that takes the restart.</param>
/// <param name="Ball">Where the ball is placed while the restart is set up, in absolute pitch coordinates.</param>
internal readonly record struct TickRestart(TickMatchPhase Kind, MatchSide Side, SpatialPoint Ball);

/// <summary>
/// The restarts and set pieces state machine of the tick engine: which dead ball is being set up, for how long, and whose
/// it is (`tick-engine-v1`, Milestone 7).
/// </summary>
/// <remarks>
/// <para>
/// The machine is the one place that knows what a goal, a ball over the line or a foul means. The tick loop feeds it the
/// moments of play as it finds them (<see cref="Goal"/>, <see cref="TouchlineOut"/>, <see cref="GoalLineOut"/>,
/// <see cref="Foul"/>, <see cref="KickOff"/>, <see cref="HalfTime"/>) and steps it once per tick with <see cref="Step"/>;
/// it answers with the phase the match is in, the restart that is being set up and, on the tick the hold ends, the fact
/// that the kick is due.
/// </para>
/// <para>
/// <b>Whistle and transition.</b> Every restart is set up over a natural hold of whistle, jog and assembly: 0.8 s to
/// 1.5 s (8 to 15 ticks). The players are steered into the set-piece picture of <see cref="TickSetPieces.Assign"/> for
/// the whole hold, and the moment the hold ends the phase returns to <see cref="TickMatchPhase.OpenPlay"/> and
/// <see cref="IsKickDue"/> is set so the loop takes the kick through <see cref="TickSetPieces.Kick"/> on that very tick,
/// which is what makes the transition smooth instead of a teleport. A goal is celebrated for 2 s and a goal-line
/// restart chains: celebration, then the conceding side's kick-off, then play.
/// </para>
/// <para>
/// The machine never touches the ball or the players and never draws from a stream: it is pure state, so a match replays
/// to the same tick on every machine.
/// </para>
/// </remarks>
internal sealed class TickMatchStateMachine
{
    /// <summary>How long a kick-off is set up, in ticks (1.2 s).</summary>
    public const int KickOffHoldTicks = 12;

    /// <summary>How long a goal kick is set up, in ticks (1.0 s).</summary>
    public const int GoalKickHoldTicks = 10;

    /// <summary>How long a throw-in is set up, in ticks (0.8 s): the shortest hold, since the ball never left the pitch.</summary>
    public const int ThrowInHoldTicks = 8;

    /// <summary>How long a corner is set up, in ticks (1.2 s).</summary>
    public const int CornerHoldTicks = 12;

    /// <summary>How long a free kick is set up, in ticks (1.2 s).</summary>
    public const int FreeKickHoldTicks = 12;

    /// <summary>How long a penalty is set up, in ticks (1.5 s): the longest hold, for the walk from the spot.</summary>
    public const int PenaltyHoldTicks = 15;

    /// <summary>How long a goal is celebrated, in ticks (2.0 s).</summary>
    public const int GoalCelebrationTicks = 20;

    /// <summary>How long the half-time break is held before the second half is set up, in ticks (1.5 s).</summary>
    public const int HalfTimeTicks = 15;

    /// <summary>Gets the phase the match is in.</summary>
    public TickMatchPhase Phase { get; private set; } = TickMatchPhase.OpenPlay;

    /// <summary>Gets the restart being set up, or the last one taken (`Kind` is <see cref="TickMatchPhase.OpenPlay"/> before the first).</summary>
    public TickRestart Restart { get; private set; }

    /// <summary>Gets the ticks left in the current setup hold, or 0 in open play.</summary>
    public int HoldTicksRemaining { get; private set; }

    /// <summary>Gets a value indicating whether the ball is dead: a restart is being set up or a kick is due.</summary>
    public bool IsDeadBall => Phase != TickMatchPhase.OpenPlay;

    /// <summary>Gets a value indicating whether the setup hold has ended and the loop must take the kick this tick.</summary>
    public bool IsKickDue { get; private set; }

    /// <summary>Gets how long a given phase is held, in ticks.</summary>
    /// <param name="kind">The phase.</param>
    public static int HoldTicksFor(TickMatchPhase kind) => kind switch
    {
        TickMatchPhase.KickOffPending => KickOffHoldTicks,
        TickMatchPhase.GoalKickPending => GoalKickHoldTicks,
        TickMatchPhase.CornerPending => CornerHoldTicks,
        TickMatchPhase.ThrowInPending => ThrowInHoldTicks,
        TickMatchPhase.FreeKickPending => FreeKickHoldTicks,
        TickMatchPhase.PenaltyPending => PenaltyHoldTicks,
        TickMatchPhase.GoalCelebration => GoalCelebrationTicks,
        TickMatchPhase.HalfTime => HalfTimeTicks,
        _ => 0,
    };

    /// <summary>Sets up a kick-off for a side: the home side starts the match, the away side the second half, and the conceding side after a goal.</summary>
    /// <param name="side">The side that kicks off.</param>
    public void KickOff(MatchSide side) => Begin(TickMatchPhase.KickOffPending, side, SpatialPoint.Center);

    /// <summary>Celebrates a goal, after which the conceding side kicks off.</summary>
    /// <param name="scoringSide">The side that scored.</param>
    public void Goal(MatchSide scoringSide) => Begin(TickMatchPhase.GoalCelebration, Other(scoringSide), SpatialPoint.Center);

    /// <summary>Gives a throw-in to the side that did not put the ball over the touchline, at the point it crossed.</summary>
    /// <param name="crossing">Where the ball crossed the line, in absolute pitch coordinates.</param>
    /// <param name="lastTouch">The side that touched the ball last.</param>
    public void TouchlineOut(SpatialPoint crossing, MatchSide lastTouch) =>
        Begin(
            TickMatchPhase.ThrowInPending,
            Other(lastTouch),
            new SpatialPoint(
                Math.Clamp(crossing.X, 0, SpatialPitch.PitchLength),
                crossing.Y < SpatialPitch.GoalYCenter ? 0 : SpatialPitch.PitchWidth));

    /// <summary>
    /// Gives a corner to the attacking side when a defender put the ball behind his own goal line, or a goal kick to the
    /// defending side when an attacker did.
    /// </summary>
    /// <param name="homeGoalLine">True when the ball crossed the home side's goal line (X = 0).</param>
    /// <param name="crossing">Where the ball crossed the line, in absolute pitch coordinates.</param>
    /// <param name="lastTouch">The side that touched the ball last.</param>
    public void GoalLineOut(bool homeGoalLine, SpatialPoint crossing, MatchSide lastTouch)
    {
        var defenders = homeGoalLine ? MatchSide.Home : MatchSide.Away;
        var lowCorner = crossing.Y < SpatialPitch.GoalYCenter;

        if (lastTouch == defenders)
        {
            // A defender put it behind: a corner on the side of the pitch it went out, the ball inside the arc.
            Begin(
                TickMatchPhase.CornerPending,
                Other(defenders),
                new SpatialPoint(
                    homeGoalLine ? TickSetPieces.CornerInset : SpatialPitch.PitchLength - TickSetPieces.CornerInset,
                    lowCorner ? TickSetPieces.CornerInset : SpatialPitch.PitchWidth - TickSetPieces.CornerInset));
        }
        else
        {
            // An attacker put it behind: a goal kick, the ball on the six-yard line.
            Begin(
                TickMatchPhase.GoalKickPending,
                defenders,
                new SpatialPoint(
                    homeGoalLine ? TickSetPieces.GoalAreaDepth : SpatialPitch.PitchLength - TickSetPieces.GoalAreaDepth,
                    SpatialPitch.GoalYCenter));
        }
    }

    /// <summary>
    /// Gives the fouled side a free kick at the spot, or a penalty when the foul was inside the area the side attacks.
    /// </summary>
    /// <param name="spot">Where the foul was committed, in absolute pitch coordinates.</param>
    /// <param name="fouledSide">The side that was fouled.</param>
    public void Foul(SpatialPoint spot, MatchSide fouledSide)
    {
        var intoAwayGoal = fouledSide == MatchSide.Home;

        // The area the fouled side attacks: the away end for the home side, the home end for the away side.
        var inAreaX = intoAwayGoal
            ? spot.X >= SpatialPitch.PitchLength - SpatialPitch.PenaltyBoxWidth
            : spot.X <= SpatialPitch.PenaltyBoxWidth;
        var inAreaBand = spot.Y >= SpatialPitch.PenaltyBoxYMin && spot.Y <= SpatialPitch.PenaltyBoxYMax;
        var penalty = inAreaX && inAreaBand;

        Begin(
            penalty ? TickMatchPhase.PenaltyPending : TickMatchPhase.FreeKickPending,
            fouledSide,
            penalty
                ? new SpatialPoint(
                    intoAwayGoal ? SpatialPitch.PenaltySpotAwayX : SpatialPitch.PenaltySpotHomeX,
                    SpatialPitch.PenaltySpotY)
                : spot);
    }

    /// <summary>Ends the first half; the second half begins with the away side's kick-off.</summary>
    public void HalfTime() => Begin(TickMatchPhase.HalfTime, MatchSide.Away, SpatialPoint.Center);

    /// <summary>Advances the dead-ball hold one tick (100 ms).</summary>
    /// <remarks>
    /// A celebration or the interval runs down and then chains into the conceding (or away) side's kick-off hold; any
    /// other pending phase runs down and then the kick is due, the phase being open play again. In open play, and while a
    /// due kick has not been taken, the tick changes nothing.
    /// </remarks>
    /// <returns>True when the phase changed this tick.</returns>
    public bool Step()
    {
        if (Phase == TickMatchPhase.OpenPlay || IsKickDue)
        {
            return false;
        }

        if (--HoldTicksRemaining > 0)
        {
            return false;
        }

        switch (Phase)
        {
            case TickMatchPhase.GoalCelebration:
            case TickMatchPhase.HalfTime:
                KickOff(Restart.Side);
                break;

            default:
                Phase = TickMatchPhase.OpenPlay;
                IsKickDue = true;
                break;
        }

        return true;
    }

    /// <summary>Clears the due kick once the loop has taken it, so the match is in open play.</summary>
    /// <remarks>The restart record itself stays until the next restart replaces it, because the replay keeps reading it.</remarks>
    public void KickTaken()
    {
        IsKickDue = false;
        HoldTicksRemaining = 0;
    }

    private void Begin(TickMatchPhase kind, MatchSide side, SpatialPoint ball)
    {
        Phase = kind;
        Restart = new TickRestart(kind, side, ball);
        HoldTicksRemaining = HoldTicksFor(kind);
        IsKickDue = false;
    }

    private static MatchSide Other(MatchSide side) => side == MatchSide.Home ? MatchSide.Away : MatchSide.Home;
}

/// <summary>What the taker does with a restart ball.</summary>
/// <remarks>The action is the fact the replay and the commentary read; the ball itself is launched physically either way.</remarks>
internal enum TickRestartAction
{
    /// <summary>Nothing was kicked: the phase was not a restart that is taken by a player.</summary>
    None = 0,

    /// <summary>A ball played to a teammate: the kick-off back into midfield, the short goal kick, the simple free kick.</summary>
    Pass = 1,

    /// <summary>A long ball played downfield, from a goal kick that goes long.</summary>
    LongBall = 2,

    /// <summary>A delivery into the penalty area: the corner, and the free kick that is crossed.</summary>
    Cross = 3,

    /// <summary>A strike at goal: the direct free kick and the penalty.</summary>
    Shot = 4,

    /// <summary>A throw back into play from the touchline.</summary>
    Throw = 5,
}

/// <summary>Everything the set-piece setup and the restart kick read about the moment before a dead ball is taken.</summary>
/// <remarks>
/// The two sides are the taking side and the side defending the restart, each with index 0 the goalkeeper as the rest of
/// the tick engine has it. Positions and skills are as they stand when the setup starts; <see cref="Kick"/> is given the
/// same situation again once the players have walked into their places, so what it targets is where they are.
/// </remarks>
internal readonly ref struct TickRestartSituation
{
    /// <summary>Gets the restart's kind: which pending phase is being taken.</summary>
    public required TickMatchPhase Kind { get; init; }

    /// <summary>Gets a value indicating whether the taking side is the home side (attacking towards high X).</summary>
    public required bool IsHome { get; init; }

    /// <summary>Gets where the ball is placed, in absolute pitch coordinates.</summary>
    public required SpatialPoint Ball { get; init; }

    /// <summary>Gets the taking side's players, as they stand.</summary>
    public required ReadOnlySpan<TickPlayerState> Takers { get; init; }

    /// <summary>Gets the taking side's board positions, in the same order (they carry the position family).</summary>
    public required ReadOnlySpan<TickAnchorSpec> TakerSpecs { get; init; }

    /// <summary>Gets the taking side's skills, in the same order.</summary>
    public required ReadOnlySpan<TickPlayerSkills> TakerSkills { get; init; }

    /// <summary>Gets the defending side's players, as they stand.</summary>
    public required ReadOnlySpan<TickPlayerState> Defenders { get; init; }

    /// <summary>Gets the defending side's board positions, in the same order, in their own point of view.</summary>
    public required ReadOnlySpan<TickAnchorSpec> DefenderSpecs { get; init; }

    /// <summary>Gets the defending side's skills, in the same order.</summary>
    public required ReadOnlySpan<TickPlayerSkills> DefenderSkills { get; init; }
}

/// <summary>Who takes the restart, and who stands over the ball with him.</summary>
/// <param name="Taker">The index of the player who plays the ball, in the taking side's order.</param>
/// <param name="Partner">The index of the second player at the centre spot at a kick-off, or -1.</param>
internal readonly record struct TickRestartPlan(int Taker, int Partner);

/// <summary>
/// The set-piece pictures and restart kicks of the tick engine: where the 22 players stand for each dead ball, and what
/// the taker does with it (`tick-engine-v1`, Milestone 7).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Assign"/> fills one target per player per side for the setup hold — the kick-off holds all 20 players in
/// their own halves with two at the centre spot, a goal kick places the ball on the six-yard line and splits the
/// centre-backs wide, a corner puts the headers in the box and a marker on each of them, a penalty sends everyone but the
/// taker and the goalkeeper behind the ball, and a free kick in shooting range builds a wall 9.15 m from the ball. Every
/// picture is worked out in the taking side's own point of view and mirrored for the away side, as the rest of the tick
/// engine does.
/// </para>
/// <para>
/// <see cref="Kick"/> then plays the ball the way the restart is really taken: the kick-off backwards into midfield, the
/// goal kick short to a split centre-back or long downfield, the corner crossed to the best header, the throw rolled to
/// the nearest teammate, the free kick struck at goal or delivered, and the penalty struck at the goal mouth. Every kick
/// leaves the ball rotated from its aim by a draw within the kick's error angle, scaled by the taker's skill and
/// composure exactly as the ball-carrier brain does it, so a good taker misses his spot less.
/// </para>
/// <para>
/// Everything is integer arithmetic over spans and stack buffers, so a dead ball allocates nothing.
/// </para>
/// </remarks>
internal static class TickSetPieces
{
    /// <summary>The margin every ordinary placement keeps from a touchline or goal line, in pitch units.</summary>
    public const int Margin = TickTacticalGeometry.Margin;

    /// <summary>The depth of the goal area (the six-yard box), in pitch units (about 5.5 m).</summary>
    public const int GoalAreaDepth = 524;

    /// <summary>How far in from a corner flag the ball is placed, in pitch units: inside the corner arc.</summary>
    public const int CornerInset = 30;

    /// <summary>How far behind the ball a standing taker waits, in pitch units (0.6 m).</summary>
    public const int TakerStandOff = 60;

    /// <summary>How far behind the centre spot the kick-off taker stands, in pitch units, inside the centre circle.</summary>
    public const int KickOffTakerStandOff = 45;

    /// <summary>How far behind the centre spot the kick-off partner stands, in pitch units.</summary>
    public const int KickOffPartnerStandOff = 250;

    /// <summary>The own-half limit at a kick-off: the deepest into the opponent's half a player may stand, in pitch units.</summary>
    public const int KickOffOwnHalfLimit = 4_900;

    /// <summary>The opponent-half limit at a kick-off: the nearest to the halfway line an opponent may stand, in pitch units.</summary>
    public const int KickOffOpponentHalfLimit = 5_100;

    /// <summary>The clearance every opponent keeps from the centre spot at a kick-off, in pitch units (9.15 m).</summary>
    public const int KickOffCircleRadius = 875;

    /// <summary>How far out from the goal line the split centre-backs stand at a goal kick, in pitch units.</summary>
    public const int GoalKickCentreBackX = 760;

    /// <summary>The lower touchline-side spot the centre-backs split to at a goal kick, in pitch units.</summary>
    public const int GoalKickSplitYMin = 2_400;

    /// <summary>The upper touchline-side spot the centre-backs split to at a goal kick, in pitch units.</summary>
    public const int GoalKickSplitYMax = 4_600;

    /// <summary>How far up the pitch the full-backs stand at a goal kick, in the taking side's own X, in pitch units.</summary>
    public const int GoalKickFullBackX = 1_900;

    /// <summary>How far up the pitch the midfielders stand at a goal kick, in the taking side's own X, in pitch units.</summary>
    public const int GoalKickMidfieldX = 3_400;

    /// <summary>How far up the pitch the forwards stand at a goal kick, in the taking side's own X, in pitch units.</summary>
    public const int GoalKickAttackX = 4_400;

    /// <summary>The nearest to the goal line an opponent may stand at a goal kick (out of the area, with a margin), in pitch units.</summary>
    public const int GoalKickOpponentClearance = 1_750;

    /// <summary>The chance of a short goal kick at a keeper of Passing 0, in basis points.</summary>
    public const int GoalKickShortBaseBasisPoints = 3_500;

    /// <summary>What each point of the keeper's Passing adds to the chance of a short goal kick, in basis points.</summary>
    public const int GoalKickShortPassingStep = 125;

    /// <summary>How many attackers assemble in the penalty area for a corner.</summary>
    public const int CornerAttackersInBox = 5;

    /// <summary>The X of the six-yard-box spot the most dangerous corner header takes, in the taking side's own X.</summary>
    public const int CornerSixYardX = 9_500;

    /// <summary>The X of the near-post corner header's spot, in the taking side's own X.</summary>
    public const int CornerNearPostX = 9_350;

    /// <summary>The X of the far-post corner header's spot, in the taking side's own X.</summary>
    public const int CornerFarPostX = 9_550;

    /// <summary>The X of the edge-of-the-area corner header's spot, in the taking side's own X.</summary>
    public const int CornerEdgeX = 8_600;

    /// <summary>How far inside a post a corner header stands, in pitch units.</summary>
    public const int CornerPostInset = 50;

    /// <summary>How far a corner marker stands goal-side of the man he marks at his loosest, in pitch units.</summary>
    public const int CornerMarkerStandOff = 570;

    /// <summary>How much closer a corner marker stands per point of his Marking, in pitch units.</summary>
    public const int CornerMarkerMarkingStep = 15;

    /// <summary>How far in front of the goal line the defending goalkeeper stands at a corner, in pitch units.</summary>
    public const int CornerKeeperDepth = 160;

    /// <summary>How far off the middle of his goal the defending goalkeeper stands at a corner, towards the near post, in pitch units.</summary>
    public const int CornerKeeperInset = 250;

    /// <summary>How far from a post the men on the posts stand along the goal line, in pitch units.</summary>
    public const int CornerPostStandInset = 30;

    /// <summary>How far either side of the middle the zonal defenders stand in the six-yard box at a corner, in pitch units.</summary>
    public const int CornerZonalYOffset = 700;

    /// <summary>How far up the pitch the corner's counter-attacking outlet stands, in the taking side's own X, in pitch units.</summary>
    public const int CornerOutletX = 7_600;

    /// <summary>How far behind the penalty spot the taker starts his run, in pitch units.</summary>
    public const int PenaltyRunUp = 130;

    /// <summary>The nearest to the defending goal every other player may stand at a penalty, in the taking side's own X.</summary>
    /// <remarks>
    /// Behind this line a player is at least 1,000 units from the spot, which clears the 9.15 m arc, and outside the
    /// penalty area, whose edge is another 450 units further on. The two conditions the law asks for are both met by one
    /// distance.
    /// </remarks>
    public const int PenaltyRestLimit = 7_900;

    /// <summary>How far in front of the goal line the defending goalkeeper stands at a penalty, in pitch units.</summary>
    public const int PenaltyKeeperDepth = 50;

    /// <summary>How far inside a post a penalty is aimed, in pitch units: far enough that an average taker's error still finds the goal.</summary>
    public const int PenaltyAimInset = 250;

    /// <summary>The own-X from which a free kick is in shooting range: the same threshold as the legacy rules' <c>FreeKickShootingRangeX</c>.</summary>
    public const int FreeKickShootingRangeX = 6_500;

    /// <summary>The clearance every player not in the wall keeps from the ball at a free kick, in pitch units.</summary>
    public const int FreeKickClearRadius = 1_000;

    /// <summary>The wall's distance from the ball at a free kick, in pitch units (9.15 m).</summary>
    public const int WallDistance = 871;

    /// <summary>The men in a free-kick wall.</summary>
    public const int WallSize = 4;

    /// <summary>The spacing between the men in a wall, in pitch units.</summary>
    public const int WallSpacing = 60;

    /// <summary>How far inside the near post the wall is set up to cover, in pitch units.</summary>
    public const int WallAimInset = 100;

    /// <summary>How far in front of the goal line the defending goalkeeper stands at a free kick, in pitch units.</summary>
    public const int FreeKickKeeperDepth = 160;

    /// <summary>The share of the ball's offset from the middle the free-kick keeper shades across his line by (a quarter).</summary>
    public const int FreeKickKeeperShift = 4;

    /// <summary>How far inside a post the free-kick keeper may stand, in pitch units.</summary>
    public const int KeeperPostClearance = 150;

    /// <summary>The chance a free kick in shooting range is struck directly at goal rather than delivered, in basis points.</summary>
    public const int FreeKickDirectShotBasisPoints = 3_000;

    /// <summary>The clearance every player keeps from the ball at a throw-in, in pitch units.</summary>
    public const int ThrowClearRadius = 400;

    /// <summary>The flattest apex a long goal kick is given, in Z units.</summary>
    public const int LongBallApexMin = 8;

    /// <summary>The highest apex a long goal kick is given, in Z units.</summary>
    public const int LongBallApexMax = 45;

    /// <summary>The highest chance a short goal kick is given whatever the keeper's passing, in basis points.</summary>
    public const int GoalKickShortMaximumBasisPoints = 9_000;

    /// <summary>The spots the corner's spare attackers take when the box is full, in the taking side's own point of view.</summary>
    private static readonly SpatialPoint[] CornerOutsideSpots =
    [
        new(8_200, 2_600),
        new(8_200, 4_400),
        new(7_800, 3_500),
        new(7_200, 3_500),
    ];

    /// <summary>Works out the set-piece picture: one target per player per side, and who takes the restart.</summary>
    /// <param name="situation">The moment before the dead ball is set up.</param>
    /// <param name="takers">Receives one target per taking-side player, in the same order, in absolute pitch units.</param>
    /// <param name="defenders">Receives one target per defending-side player, in the same order, in absolute pitch units.</param>
    /// <returns>Who takes the restart.</returns>
    public static TickRestartPlan Assign(
        in TickRestartSituation situation,
        Span<SpatialPoint> takers,
        Span<SpatialPoint> defenders)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(situation.Takers.Length, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(situation.Defenders.Length, TickTacticalGeometry.TeamSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(takers.Length, situation.Takers.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(defenders.Length, situation.Defenders.Length);

        var ball = View(situation.Ball, situation.IsHome);

        var plan = situation.Kind switch
        {
            TickMatchPhase.KickOffPending => PlaceKickOff(situation, takers, defenders),
            TickMatchPhase.GoalKickPending => PlaceGoalKick(situation, takers, defenders),
            TickMatchPhase.CornerPending => PlaceCorner(situation, ball, takers, defenders),
            TickMatchPhase.ThrowInPending => PlaceThrowIn(situation, ball, takers, defenders),
            TickMatchPhase.FreeKickPending => PlaceFreeKick(situation, ball, takers, defenders),
            TickMatchPhase.PenaltyPending => PlacePenalty(situation, takers, defenders),
            _ => PlaceShapes(situation, takers, defenders),
        };

        if (!situation.IsHome)
        {
            Mirror(takers);
            Mirror(defenders);
        }

        return plan;
    }

    /// <summary>Plays the restart the way it is really taken, and says what the taker did.</summary>
    /// <param name="situation">The moment the kick is taken, with the players in their set-piece places.</param>
    /// <param name="plan">Who takes it, as <see cref="Assign"/> found.</param>
    /// <param name="ball">The ball, placed on the restart's spot; it is launched.</param>
    /// <param name="random">The play stream.</param>
    /// <param name="receiver">Receives the teammate the ball was played to, or -1 for a strike at goal.</param>
    /// <returns>What the taker did.</returns>
    public static TickRestartAction Kick(
        in TickRestartSituation situation,
        in TickRestartPlan plan,
        TickBallPhysics ball,
        Pcg32 random,
        out int receiver)
    {
        ArgumentNullException.ThrowIfNull(ball);
        ArgumentNullException.ThrowIfNull(random);

        receiver = -1;

        if (plan.Taker < 0 || plan.Taker >= situation.Takers.Length)
        {
            return TickRestartAction.None;
        }

        var skills = situation.TakerSkills[plan.Taker];
        var pressure = TickBallCarrierBrain.EffectivePressure(
            TickBallCarrierBrain.MeasurePressure(situation.Takers[plan.Taker], situation.Defenders),
            skills.Composure);

        switch (situation.Kind)
        {
            case TickMatchPhase.KickOffPending:
                {
                    receiver = plan.Partner >= 0 ? plan.Partner : NearestToBall(situation.Takers, situation.Ball, plan.Taker);
                    var partner = situation.Takers[receiver];

                    Strike(
                        ball,
                        TickSpatialUnits.ToUnits(partner.X),
                        TickSpatialUnits.ToUnits(partner.Y),
                        TickBallCarrierBrain.ErrorAngle(skills.Passing, skills.Technique, TickBallCarrierBrain.PassBaseError, pressure),
                        TickSpatialUnits.SpeedToFixedPerTick(TickBallCarrierBrain.PassArrivalCentimetresPerSecond),
                        apex: 0,
                        lofted: false,
                        clampAim: true,
                        random);

                    return TickRestartAction.Pass;
                }

            case TickMatchPhase.GoalKickPending:
                {
                    var shortChance = Math.Min(
                        GoalKickShortMaximumBasisPoints,
                        GoalKickShortBaseBasisPoints + (GoalKickShortPassingStep * skills.Passing));

                    if (random.NextBasisPoints() < shortChance)
                    {
                        receiver = NearestOfFamily(situation.Takers, situation.TakerSpecs, situation.Ball, MatchPositionFamily.Defence);
                    }

                    if (receiver >= 0)
                    {
                        var shortTarget = situation.Takers[receiver];

                        Strike(
                            ball,
                            TickSpatialUnits.ToUnits(shortTarget.X),
                            TickSpatialUnits.ToUnits(shortTarget.Y),
                            TickBallCarrierBrain.ErrorAngle(skills.Passing, skills.Technique, TickBallCarrierBrain.PassBaseError, pressure),
                            TickSpatialUnits.SpeedToFixedPerTick(TickBallCarrierBrain.PassArrivalCentimetresPerSecond),
                            apex: 0,
                            lofted: false,
                            clampAim: true,
                            random);

                        return TickRestartAction.Pass;
                    }

                    receiver = FurthestUp(situation.Takers, situation.IsHome);
                    var target = situation.Takers[receiver];
                    var distance = (int)(SpatialMath.Sqrt(Distance(situation.Takers[plan.Taker], target)) / TickSpatialUnits.FixedScale);
                    var apex = Math.Clamp(
                        TickBallCarrierBrain.LoftApexBase + (distance / TickBallCarrierBrain.LoftApexDistance),
                        LongBallApexMin,
                        LongBallApexMax);

                    Strike(
                        ball,
                        TickSpatialUnits.ToUnits(target.X),
                        TickSpatialUnits.ToUnits(target.Y),
                        TickBallCarrierBrain.ErrorAngle(skills.Passing, skills.Technique, TickBallCarrierBrain.PassBaseError, pressure),
                        0,
                        apex,
                        lofted: true,
                        clampAim: true,
                        random);

                    return TickRestartAction.LongBall;
                }

            case TickMatchPhase.CornerPending:
                {
                    receiver = BestHeader(situation.TakerSkills, plan.Taker);
                    var target = situation.Takers[receiver];

                    Strike(
                        ball,
                        TickSpatialUnits.ToUnits(target.X),
                        TickSpatialUnits.ToUnits(target.Y),
                        TickBallCarrierBrain.ErrorAngle(skills.Crossing, skills.Technique, TickBallCarrierBrain.CrossBaseError, pressure),
                        0,
                        TickBallCarrierBrain.CrossApex,
                        lofted: true,
                        clampAim: true,
                        random);

                    return TickRestartAction.Cross;
                }

            case TickMatchPhase.FreeKickPending:
                {
                    var ownBallX = Own(ball.UnitX, situation.IsHome);

                    if (ownBallX >= FreeKickShootingRangeX)
                    {
                        if (random.NextBasisPoints() < FreeKickDirectShotBasisPoints)
                        {
                            var ownBallY = Own(ball.UnitY, situation.IsHome);
                            var aimY = ownBallY < SpatialPitch.GoalYCenter
                                ? SpatialPitch.GoalYMax - TickBallCarrierBrain.ShotAimOffset
                                : SpatialPitch.GoalYMin + TickBallCarrierBrain.ShotAimOffset;
                            var aim = View(new SpatialPoint(SpatialPitch.PitchLength + TickBallCarrierBrain.ShotOvershoot, aimY), situation.IsHome);

                            Strike(
                                ball,
                                aim.X,
                                aim.Y,
                                TickBallCarrierBrain.ErrorAngle(skills.Finishing, skills.Technique, TickBallCarrierBrain.ShotBaseError, pressure),
                                TickSpatialUnits.SpeedToFixedPerTick(TickBallCarrierBrain.ShotArrivalCentimetresPerSecond),
                                apex: 0,
                                lofted: false,
                                clampAim: false,
                                random);

                            return TickRestartAction.Shot;
                        }

                        var delivery = View(new SpatialPoint(SpatialPitch.PenaltySpotAwayX, SpatialPitch.GoalYCenter), situation.IsHome);
                        receiver = NearestToPoint(situation.Takers, delivery, plan.Taker);

                        Strike(
                            ball,
                            delivery.X,
                            delivery.Y,
                            TickBallCarrierBrain.ErrorAngle(skills.Crossing, skills.Technique, TickBallCarrierBrain.CrossBaseError, pressure),
                            0,
                            TickBallCarrierBrain.CrossApex,
                            lofted: true,
                            clampAim: true,
                            random);

                        return TickRestartAction.Cross;
                    }

                    receiver = NearestToBall(situation.Takers, situation.Ball, plan.Taker);
                    var simple = situation.Takers[receiver];

                    Strike(
                        ball,
                        TickSpatialUnits.ToUnits(simple.X),
                        TickSpatialUnits.ToUnits(simple.Y),
                        TickBallCarrierBrain.ErrorAngle(skills.Passing, skills.Technique, TickBallCarrierBrain.PassBaseError, pressure),
                        TickSpatialUnits.SpeedToFixedPerTick(TickBallCarrierBrain.PassArrivalCentimetresPerSecond),
                        apex: 0,
                        lofted: false,
                        clampAim: true,
                        random);

                    return TickRestartAction.Pass;
                }

            case TickMatchPhase.PenaltyPending:
                {
                    var low = random.NextInt(2) == 0;
                    var aimY = low
                        ? SpatialPitch.GoalYMin + PenaltyAimInset
                        : SpatialPitch.GoalYMax - PenaltyAimInset;
                    var aim = View(new SpatialPoint(SpatialPitch.PitchLength + TickBallCarrierBrain.ShotOvershoot, aimY), situation.IsHome);

                    Strike(
                        ball,
                        aim.X,
                        aim.Y,
                        TickBallCarrierBrain.ErrorAngle(skills.Finishing, skills.Technique, TickBallCarrierBrain.ShotBaseError, pressure),
                        TickSpatialUnits.SpeedToFixedPerTick(TickBallCarrierBrain.ShotArrivalCentimetresPerSecond),
                        apex: 0,
                        lofted: false,
                        clampAim: false,
                        random);

                    return TickRestartAction.Shot;
                }

            case TickMatchPhase.ThrowInPending:
                {
                    receiver = NearestToBall(situation.Takers, situation.Ball, plan.Taker);
                    var target = situation.Takers[receiver];

                    Strike(
                        ball,
                        TickSpatialUnits.ToUnits(target.X),
                        TickSpatialUnits.ToUnits(target.Y),
                        TickBallCarrierBrain.ErrorAngle(skills.Passing, skills.Technique, TickBallCarrierBrain.PassBaseError, pressure),
                        TickSpatialUnits.SpeedToFixedPerTick(TickBallCarrierBrain.PassArrivalCentimetresPerSecond),
                        apex: 0,
                        lofted: false,
                        clampAim: true,
                        random);

                    return TickRestartAction.Throw;
                }

            default:
                return TickRestartAction.None;
        }
    }

    /// <summary>Lays out a kick-off: two players at the centre spot and every other player in his own half.</summary>
    private static TickRestartPlan PlaceKickOff(in TickRestartSituation situation, Span<SpatialPoint> takers, Span<SpatialPoint> defenders)
    {
        var (taker, partner) = KickOffPair(situation.TakerSpecs);

        for (var index = 0; index < situation.Takers.Length; index++)
        {
            var spec = situation.TakerSpecs[index];

            takers[index] = index == taker
                ? new SpatialPoint((SpatialPitch.PitchLength / 2) - KickOffTakerStandOff, SpatialPitch.GoalYCenter)
                : index == partner
                    ? new SpatialPoint((SpatialPitch.PitchLength / 2) - KickOffPartnerStandOff, SpatialPitch.GoalYCenter)
                    : new SpatialPoint(
                        Math.Clamp(spec.OwnX, Margin, KickOffOwnHalfLimit),
                        Math.Clamp(spec.OwnY, Margin, SpatialPitch.PitchWidth - Margin));
        }

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            // Their board is written facing the other way, so it is mirrored into this side's point of view.
            var own = Facing(situation.DefenderSpecs[index]);
            var point = Clamped(Math.Max(own.X, KickOffOpponentHalfLimit), own.Y);

            defenders[index] = Cleared(point, SpatialPoint.Center, KickOffCircleRadius);
        }

        return new TickRestartPlan(taker, partner);
    }

    /// <summary>Lays out a goal kick: the ball on the six-yard line, the centre-backs split wide, the side stretched up the pitch.</summary>
    private static TickRestartPlan PlaceGoalKick(in TickRestartSituation situation, Span<SpatialPoint> takers, Span<SpatialPoint> defenders)
    {
        var first = -1;
        var second = -1;
        long firstDistance = 0;
        long secondDistance = 0;

        for (var index = 1; index < situation.Takers.Length; index++)
        {
            var spec = situation.TakerSpecs[index];

            if (spec.Family != MatchPositionFamily.Defence)
            {
                continue;
            }

            long offset = spec.OwnY - SpatialPitch.GoalYCenter;
            var distance = offset * offset;

            if (first < 0 || distance < firstDistance)
            {
                second = first;
                secondDistance = firstDistance;
                first = index;
                firstDistance = distance;
            }
            else if (second < 0 || distance < secondDistance)
            {
                second = index;
                secondDistance = distance;
            }
        }

        var firstIsLow = first >= 0 && situation.TakerSpecs[first].OwnY < SpatialPitch.GoalYCenter;

        for (var index = 0; index < situation.Takers.Length; index++)
        {
            var spec = situation.TakerSpecs[index];

            takers[index] = index switch
            {
                0 => new SpatialPoint(GoalAreaDepth - TakerStandOff, SpatialPitch.GoalYCenter),
                _ when index == first => new SpatialPoint(GoalKickCentreBackX, firstIsLow ? GoalKickSplitYMin : GoalKickSplitYMax),
                _ when index == second => new SpatialPoint(GoalKickCentreBackX, firstIsLow ? GoalKickSplitYMax : GoalKickSplitYMin),
                _ when spec.Family == MatchPositionFamily.Defence => Clamped(GoalKickFullBackX, spec.OwnY),
                _ when spec.Family == MatchPositionFamily.Midfield => Clamped(GoalKickMidfieldX, spec.OwnY),
                _ when spec.Family == MatchPositionFamily.Attack => Clamped(GoalKickAttackX, spec.OwnY),
                _ => Clamped(spec.OwnX, spec.OwnY),
            };
        }

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            var own = Facing(situation.DefenderSpecs[index]);
            var point = own;

            // The law keeps the opponents out of the area until the ball is in play.
            if (point.X < GoalKickOpponentClearance
                && point.Y >= SpatialPitch.PenaltyBoxYMin
                && point.Y <= SpatialPitch.PenaltyBoxYMax)
            {
                point = new SpatialPoint(GoalKickOpponentClearance, point.Y);
            }

            defenders[index] = Clamped(point.X, point.Y);
        }

        return new TickRestartPlan(0, -1);
    }

    /// <summary>Lays out a corner: the headers in the box, a marker on each of them, the taker on the arc.</summary>
    private static TickRestartPlan PlaceCorner(in TickRestartSituation situation, SpatialPoint ball, Span<SpatialPoint> takers, Span<SpatialPoint> defenders)
    {
        var taker = BestCrosser(situation.TakerSkills);

        Span<int> heading = stackalloc int[TickTacticalGeometry.TeamSize];
        Span<int> headingPlace = stackalloc int[TickTacticalGeometry.TeamSize];

        for (var index = 0; index < situation.TakerSkills.Length; index++)
        {
            heading[index] = index == 0 || index == taker
                ? int.MinValue
                : situation.TakerSkills[index].Heading + situation.TakerSkills[index].JumpingReach;
        }

        RankPositions(heading, headingPlace);

        var low = ball.Y < SpatialPitch.GoalYCenter;
        var nearPostY = low ? SpatialPitch.GoalYMin + CornerPostInset : SpatialPitch.GoalYMax - CornerPostInset;
        var farPostY = low ? SpatialPitch.GoalYMax - CornerPostInset : SpatialPitch.GoalYMin + CornerPostInset;

        Span<SpatialPoint> box = stackalloc SpatialPoint[CornerAttackersInBox];

        box[0] = new SpatialPoint(CornerSixYardX, SpatialPitch.GoalYCenter);
        box[1] = new SpatialPoint(CornerNearPostX, nearPostY);
        box[2] = new SpatialPoint(CornerFarPostX, farPostY);
        box[3] = new SpatialPoint(SpatialPitch.PenaltySpotAwayX, SpatialPitch.GoalYCenter);
        box[4] = new SpatialPoint(CornerEdgeX, SpatialPitch.GoalYCenter);

        for (var index = 0; index < situation.Takers.Length; index++)
        {
            if (index == 0)
            {
                takers[index] = Clamped(situation.TakerSpecs[index].OwnX, situation.TakerSpecs[index].OwnY);
            }
            else if (index == taker)
            {
                takers[index] = ball;
            }
            else
            {
                var place = headingPlace[index];

                takers[index] = place < CornerAttackersInBox
                    ? box[place]
                    : OutsideSpot(place - CornerAttackersInBox);
            }
        }

        Span<int> marking = stackalloc int[TickTacticalGeometry.TeamSize];
        Span<int> markingPlace = stackalloc int[TickTacticalGeometry.TeamSize];

        for (var index = 0; index < situation.DefenderSkills.Length; index++)
        {
            marking[index] = index == 0 ? int.MinValue : situation.DefenderSkills[index].Marking;
        }

        RankPositions(marking, markingPlace);

        var keeperY = low ? SpatialPitch.GoalYMin + CornerKeeperInset : SpatialPitch.GoalYMax - CornerKeeperInset;

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            if (index == 0)
            {
                defenders[index] = Clamped(SpatialPitch.PitchLength - CornerKeeperDepth, keeperY);
                continue;
            }

            var place = markingPlace[index];

            if (place < CornerAttackersInBox)
            {
                var standOff = CornerMarkerStandOff - (CornerMarkerMarkingStep * situation.DefenderSkills[index].Marking);

                defenders[index] = Clamped(box[place].X + standOff, box[place].Y);
            }
            else if (place == CornerAttackersInBox)
            {
                defenders[index] = Clamped(
                    SpatialPitch.PitchLength - CornerPostStandInset,
                    low ? SpatialPitch.GoalYMin + CornerPostStandInset : SpatialPitch.GoalYMax - CornerPostStandInset);
            }
            else if (place == CornerAttackersInBox + 1)
            {
                defenders[index] = Clamped(
                    SpatialPitch.PitchLength - CornerPostStandInset,
                    low ? SpatialPitch.GoalYMax - CornerPostStandInset : SpatialPitch.GoalYMin + CornerPostStandInset);
            }
            else if (place == CornerAttackersInBox + 2)
            {
                defenders[index] = Clamped(CornerSixYardX, SpatialPitch.GoalYCenter - CornerZonalYOffset);
            }
            else if (place == CornerAttackersInBox + 3)
            {
                defenders[index] = Clamped(CornerSixYardX, SpatialPitch.GoalYCenter + CornerZonalYOffset);
            }
            else
            {
                defenders[index] = Clamped(CornerOutletX, SpatialPitch.GoalYCenter);
            }
        }

        return new TickRestartPlan(taker, -1);
    }

    /// <summary>Lays out a throw-in: the nearest player on the spot, everyone else clear of him and holding shape.</summary>
    private static TickRestartPlan PlaceThrowIn(in TickRestartSituation situation, SpatialPoint ball, Span<SpatialPoint> takers, Span<SpatialPoint> defenders)
    {
        var taker = NearestToBall(situation.Takers, situation.Ball, exclude: -1);

        for (var index = 0; index < situation.Takers.Length; index++)
        {
            var spec = situation.TakerSpecs[index];

            takers[index] = index == taker
                ? ball
                : Cleared(Clamped(spec.OwnX, spec.OwnY), ball, ThrowClearRadius);
        }

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            var own = Facing(situation.DefenderSpecs[index]);

            defenders[index] = Cleared(Clamped(own.X, own.Y), ball, ThrowClearRadius);
        }

        return new TickRestartPlan(taker, -1);
    }

    /// <summary>Lays out a free kick: the taker behind the ball, a wall 9.15 m away in shooting range, everyone else clear.</summary>
    private static TickRestartPlan PlaceFreeKick(in TickRestartSituation situation, SpatialPoint ball, Span<SpatialPoint> takers, Span<SpatialPoint> defenders)
    {
        var inRange = ball.X >= FreeKickShootingRangeX;
        var taker = inRange ? BestDirectStriker(situation.TakerSkills) : BestCrosser(situation.TakerSkills);

        for (var index = 0; index < situation.Takers.Length; index++)
        {
            var spec = situation.TakerSpecs[index];

            takers[index] = index == taker
                ? new SpatialPoint(Math.Max(ball.X - TakerStandOff, Margin), Math.Clamp(ball.Y, Margin, SpatialPitch.PitchWidth - Margin))
                : Cleared(Clamped(spec.OwnX, spec.OwnY), ball, FreeKickClearRadius);
        }

        Span<int> wallSlot = stackalloc int[TickTacticalGeometry.TeamSize];
        wallSlot.Fill(-1);

        if (inRange)
        {
            RankNearestToBall(situation.Defenders, situation.Ball, wallSlot);
        }

        var keeperY = Math.Clamp(
            SpatialPitch.GoalYCenter + ((ball.Y - SpatialPitch.GoalYCenter) / FreeKickKeeperShift),
            SpatialPitch.GoalYMin + KeeperPostClearance,
            SpatialPitch.GoalYMax - KeeperPostClearance);

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            if (index == 0)
            {
                defenders[index] = Clamped(SpatialPitch.PitchLength - FreeKickKeeperDepth, keeperY);
                continue;
            }

            if (wallSlot[index] >= 0)
            {
                defenders[index] = WallPoint(wallSlot[index], ball);
                continue;
            }

            var own = Facing(situation.DefenderSpecs[index]);

            defenders[index] = Cleared(Clamped(own.X, own.Y), ball, FreeKickClearRadius);
        }

        return new TickRestartPlan(taker, -1);
    }

    /// <summary>Lays out a penalty: the taker behind the spot, the keeper on his line, everyone else behind the ball.</summary>
    private static TickRestartPlan PlacePenalty(in TickRestartSituation situation, Span<SpatialPoint> takers, Span<SpatialPoint> defenders)
    {
        var taker = BestPenaltyTaker(situation.TakerSkills);

        for (var index = 0; index < situation.Takers.Length; index++)
        {
            var spec = situation.TakerSpecs[index];

            takers[index] = index == taker
                ? new SpatialPoint(SpatialPitch.PenaltySpotAwayX - PenaltyRunUp, SpatialPitch.GoalYCenter)
                : Clamped(Math.Min(spec.OwnX, PenaltyRestLimit), spec.OwnY);
        }

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            if (index == 0)
            {
                defenders[index] = new SpatialPoint(SpatialPitch.PitchLength - PenaltyKeeperDepth, SpatialPitch.GoalYCenter);
                continue;
            }

            var own = Facing(situation.DefenderSpecs[index]);

            defenders[index] = Clamped(Math.Min(own.X, PenaltyRestLimit), own.Y);
        }

        return new TickRestartPlan(taker, -1);
    }

    /// <summary>Lays out everyone in his shape position: the picture for a celebration or the interval.</summary>
    private static TickRestartPlan PlaceShapes(in TickRestartSituation situation, Span<SpatialPoint> takers, Span<SpatialPoint> defenders)
    {
        for (var index = 0; index < situation.Takers.Length; index++)
        {
            var spec = situation.TakerSpecs[index];

            takers[index] = Clamped(spec.OwnX, spec.OwnY);
        }

        for (var index = 0; index < situation.Defenders.Length; index++)
        {
            var own = Facing(situation.DefenderSpecs[index]);

            defenders[index] = Clamped(own.X, own.Y);
        }

        return new TickRestartPlan(-1, -1);
    }

    /// <summary>Finds the two most central midfielders or forwards: the pair that stands over a kick-off.</summary>
    private static (int Taker, int Partner) KickOffPair(ReadOnlySpan<TickAnchorSpec> specs)
    {
        var taker = -1;
        var partner = -1;
        long takersDistance = 0;
        long partnersDistance = 0;

        for (var index = 0; index < specs.Length; index++)
        {
            if (specs[index].Family is not (MatchPositionFamily.Midfield or MatchPositionFamily.Attack))
            {
                continue;
            }

            long offsetX = specs[index].OwnX - (SpatialPitch.PitchLength / 2);
            long offsetY = specs[index].OwnY - SpatialPitch.GoalYCenter;
            var distance = (offsetX * offsetX) + (offsetY * offsetY);

            if (taker < 0 || distance < takersDistance)
            {
                partner = taker;
                partnersDistance = takersDistance;
                taker = index;
                takersDistance = distance;
            }
            else if (partner < 0 || distance < partnersDistance)
            {
                partner = index;
                partnersDistance = distance;
            }
        }

        return (taker, partner);
    }

    /// <summary>Finds the best deliverer of a dead ball: the best Crossing and Technique among the outfielders.</summary>
    private static int BestCrosser(ReadOnlySpan<TickPlayerSkills> skills)
    {
        var best = -1;
        var bestScore = int.MinValue;

        for (var index = 1; index < skills.Length; index++)
        {
            var score = skills[index].Crossing + skills[index].Technique;

            if (best < 0 || score > bestScore)
            {
                best = index;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Finds the best striker of a dead ball at goal: the best Technique and Finishing among the outfielders.</summary>
    private static int BestDirectStriker(ReadOnlySpan<TickPlayerSkills> skills)
    {
        var best = -1;
        var bestScore = int.MinValue;

        for (var index = 1; index < skills.Length; index++)
        {
            var score = (2 * skills[index].Technique) + skills[index].Finishing;

            if (best < 0 || score > bestScore)
            {
                best = index;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Finds the penalty taker: the best Finishing and Composure among the outfielders.</summary>
    private static int BestPenaltyTaker(ReadOnlySpan<TickPlayerSkills> skills)
    {
        var best = -1;
        var bestScore = int.MinValue;

        for (var index = 1; index < skills.Length; index++)
        {
            var score = (2 * skills[index].Finishing) + skills[index].Composure;

            if (best < 0 || score > bestScore)
            {
                best = index;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Finds the best header of a corner: the best Heading and JumpingReach among the outfielders but the taker.</summary>
    private static int BestHeader(ReadOnlySpan<TickPlayerSkills> skills, int taker)
    {
        var best = -1;
        var bestScore = int.MinValue;

        for (var index = 1; index < skills.Length; index++)
        {
            if (index == taker)
            {
                continue;
            }

            var score = skills[index].Heading + skills[index].JumpingReach;

            if (best < 0 || score > bestScore)
            {
                best = index;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Gets the spot a spare corner attacker takes when the box is full.</summary>
    private static SpatialPoint OutsideSpot(int slot) => CornerOutsideSpots[Math.Min(slot, CornerOutsideSpots.Length - 1)];

    /// <summary>Finds the teammate nearest a point, excluding the taker and the goalkeeper.</summary>
    private static int NearestToPoint(ReadOnlySpan<TickPlayerState> players, SpatialPoint point, int exclude)
    {
        var best = -1;
        var bestDistance = long.MaxValue;
        var x = TickSpatialUnits.ToFixed(point.X);
        var y = TickSpatialUnits.ToFixed(point.Y);

        for (var index = 1; index < players.Length; index++)
        {
            if (index == exclude)
            {
                continue;
            }

            long dx = players[index].X - x;
            long dy = players[index].Y - y;
            var distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    /// <summary>Finds the teammate nearest the ball, excluding <paramref name="exclude"/> and the goalkeeper.</summary>
    private static int NearestToBall(ReadOnlySpan<TickPlayerState> players, SpatialPoint ball, int exclude) =>
        NearestToPoint(players, ball, exclude);

    /// <summary>Finds the nearest player of a family to the ball, or -1 when the side has none.</summary>
    private static int NearestOfFamily(
        ReadOnlySpan<TickPlayerState> players,
        ReadOnlySpan<TickAnchorSpec> specs,
        SpatialPoint ball,
        MatchPositionFamily family)
    {
        var best = -1;
        var bestDistance = long.MaxValue;
        var x = TickSpatialUnits.ToFixed(ball.X);
        var y = TickSpatialUnits.ToFixed(ball.Y);

        for (var index = 1; index < players.Length; index++)
        {
            if (specs[index].Family != family)
            {
                continue;
            }

            long dx = players[index].X - x;
            long dy = players[index].Y - y;
            var distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    /// <summary>Finds the outfielder furthest up the pitch: the target of a long goal kick.</summary>
    private static int FurthestUp(ReadOnlySpan<TickPlayerState> players, bool isHome)
    {
        var best = 1;
        var bestX = int.MinValue;

        for (var index = 1; index < players.Length; index++)
        {
            var ownX = OwnFixed(players[index].X, isHome);

            if (ownX > bestX)
            {
                bestX = ownX;
                best = index;
            }
        }

        return best;
    }

    /// <summary>Ranks the outfielders by how near they stand to the ball: the first <see cref="WallSize"/> form the wall.</summary>
    /// <param name="players">The defending side's players.</param>
    /// <param name="ball">The ball, in absolute pitch coordinates.</param>
    /// <param name="wallSlot">Receives each player's place in the wall, or -1 for a player who is not in it.</param>
    private static void RankNearestToBall(ReadOnlySpan<TickPlayerState> players, SpatialPoint ball, Span<int> wallSlot)
    {
        var x = TickSpatialUnits.ToFixed(ball.X);
        var y = TickSpatialUnits.ToFixed(ball.Y);
        Span<bool> used = stackalloc bool[TickTacticalGeometry.TeamSize];

        used[0] = true;

        for (var slot = 0; slot < WallSize; slot++)
        {
            var best = -1;
            long bestDistance = long.MaxValue;

            for (var index = 1; index < players.Length; index++)
            {
                if (used[index])
                {
                    continue;
                }

                long dx = players[index].X - x;
                long dy = players[index].Y - y;
                var distance = (dx * dx) + (dy * dy);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            if (best < 0)
            {
                return;
            }

            used[best] = true;
            wallSlot[best] = slot;
        }
    }

    /// <summary>Works out where the man in a given place in the wall stands: 9.15 m from the ball, across the line to goal.</summary>
    private static SpatialPoint WallPoint(int slot, SpatialPoint ball)
    {
        var aimY = ball.Y < SpatialPitch.GoalYCenter
            ? SpatialPitch.GoalYMin + WallAimInset
            : SpatialPitch.GoalYMax - WallAimInset;
        long dx = SpatialPitch.PitchLength - ball.X;
        long dy = aimY - ball.Y;
        var length = Math.Max(1, SpatialMath.Sqrt((dx * dx) + (dy * dy)));
        var baseX = ball.X + (int)(dx * WallDistance / length);
        var baseY = ball.Y + (int)(dy * WallDistance / length);
        var across = TickTrigonometry.AngleOf(dx, dy) + TickTrigonometry.QuarterTurn;
        var offset = ((2 * slot) - (WallSize - 1)) * WallSpacing / 2;

        return Clamped(
            baseX + (int)((long)TickTrigonometry.Cos(across) * offset / TickTrigonometry.Scale),
            baseY + (int)((long)TickTrigonometry.Sin(across) * offset / TickTrigonometry.Scale));
    }

    /// <summary>Ranks every score from the highest down, writing each player's place into <paramref name="place"/>.</summary>
    private static void RankPositions(ReadOnlySpan<int> scores, Span<int> place)
    {
        Span<bool> used = stackalloc bool[TickTacticalGeometry.TeamSize];

        for (var position = 0; position < scores.Length; position++)
        {
            var best = -1;

            for (var index = 0; index < scores.Length; index++)
            {
                if (!used[index] && (best < 0 || scores[index] > scores[best]))
                {
                    best = index;
                }
            }

            used[best] = true;
            place[best] = position;
        }
    }

    /// <summary>Plays a kick at a target, rotated by a draw within the kick's error angle.</summary>
    /// <param name="ball">The ball, launched.</param>
    /// <param name="targetXUnits">The aim's X, in pitch units; a strike at goal aims past the line.</param>
    /// <param name="targetYUnits">The aim's Y, in pitch units.</param>
    /// <param name="errorAngle">The most the kick leaves its aim by either way, in binary angle units.</param>
    /// <param name="arrivalSpeedFixed">The speed a rolling ball arrives at, in fixed units per tick.</param>
    /// <param name="apex">The apex of a lofted ball, in Z units.</param>
    /// <param name="lofted">True to loft the ball, false to roll it.</param>
    /// <param name="clampAim">True to keep the rotated aim on the grass; false for a strike, which is aimed at the goal.</param>
    /// <param name="random">The play stream.</param>
    private static void Strike(
        TickBallPhysics ball,
        int targetXUnits,
        int targetYUnits,
        int errorAngle,
        int arrivalSpeedFixed,
        int apex,
        bool lofted,
        bool clampAim,
        Pcg32 random)
    {
        var turn = random.NextRange(-errorAngle, errorAngle);
        var fromX = ball.UnitX;
        var fromY = ball.UnitY;
        long dx = (long)targetXUnits - fromX;
        long dy = (long)targetYUnits - fromY;
        var cos = TickTrigonometry.Cos(turn);
        var sin = TickTrigonometry.Sin(turn);
        var aimX = fromX + (int)(((dx * cos) - (dy * sin)) / TickTrigonometry.Scale);
        var aimY = fromY + (int)(((dx * sin) + (dy * cos)) / TickTrigonometry.Scale);

        if (clampAim)
        {
            aimX = Math.Clamp(aimX, Margin, SpatialPitch.PitchLength - Margin);
            aimY = Math.Clamp(aimY, Margin, SpatialPitch.PitchWidth - Margin);
        }

        if (lofted)
        {
            ball.LaunchLofted(aimX, aimY, apex);
        }
        else
        {
            ball.LaunchRolling(aimX, aimY, arrivalSpeedFixed);
        }
    }

    /// <summary>Mirrors a point into (or out of) the taking side's own point of view: the call is its own inverse.</summary>
    private static SpatialPoint View(SpatialPoint point, bool isHome) =>
        isHome
            ? point
            : new SpatialPoint(SpatialPitch.PitchLength - point.X, SpatialPitch.PitchWidth - point.Y);

    /// <summary>Mirrors a defender's board position, written in his own point of view, into the taking side's view.</summary>
    private static SpatialPoint Facing(in TickAnchorSpec spec) => View(new SpatialPoint(spec.OwnX, spec.OwnY), isHome: false);

    /// <summary>Mirrors a whole side's targets for an away side, whose own point of view is the mirrored pitch.</summary>
    private static void Mirror(Span<SpatialPoint> points)
    {
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = View(points[index], isHome: false);
        }
    }

    /// <summary>Keeps a place on the grass, a margin in from every line.</summary>
    private static SpatialPoint Clamped(int x, int y) =>
        new(Math.Clamp(x, Margin, SpatialPitch.PitchLength - Margin), Math.Clamp(y, Margin, SpatialPitch.PitchWidth - Margin));

    /// <summary>Pushes a place out of a circle around the ball, so nobody stands over the dead ball.</summary>
    private static SpatialPoint Cleared(SpatialPoint point, SpatialPoint ball, int radius)
    {
        if (radius <= 0)
        {
            return point;
        }

        long dx = (long)point.X - ball.X;
        long dy = (long)point.Y - ball.Y;
        var distance = SpatialMath.Sqrt((dx * dx) + (dy * dy));

        if (distance >= radius)
        {
            return point;
        }

        if (distance == 0)
        {
            return new SpatialPoint(ball.X, Math.Clamp(ball.Y + radius, Margin, SpatialPitch.PitchWidth - Margin));
        }

        return Clamped(
            ball.X + (int)(dx * radius / distance),
            ball.Y + (int)(dy * radius / distance));
    }

    /// <summary>Gets the squared distance between two players, in fixed units.</summary>
    private static long Distance(in TickPlayerState one, in TickPlayerState other)
    {
        long dx = one.X - other.X;
        long dy = one.Y - other.Y;

        return (dx * dx) + (dy * dy);
    }

    /// <summary>Converts an X in fixed units between the pitch's and a side's own point of view, where 0 is its own goal line.</summary>
    private static int OwnFixed(int xFixed, bool isHome) => isHome ? xFixed : TickSpatialUnits.PitchLengthFixed - xFixed;

    /// <summary>Converts an X in pitch units between the pitch's and a side's own point of view, where 0 is its own goal line.</summary>
    private static int Own(int xUnits, bool isHome) => isHome ? xUnits : SpatialPitch.PitchLength - xUnits;
}

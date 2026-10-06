using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>What the shape is arranged around (`replay-v4`).</summary>
/// <param name="Mode">How the players stand.</param>
/// <param name="Side">The side with the ball, or taking the set piece, or celebrating.</param>
/// <param name="Anchor">The corner flag, the free-kick or penalty spot, or the goal a celebration begins at.</param>
/// <param name="Scorer">The entity celebrating, or -1.</param>
/// <param name="Taker">The entity taking the set piece, or -1 (`replay-v6`).</param>
/// <param name="Arrived">Whether the runners of a set piece have run in to the places they take it from, or still wait short of them (`replay-v6`).</param>
internal readonly record struct ShapeState(FormationMode Mode, MatchSide Side, Vec Anchor, int Scorer = -1, int Taker = -1, bool Arrived = false);

/// <summary>How a side stands as a block, chosen from where the ball is and who has it (`replay-v6`).</summary>
internal enum BlockPhase
{
    /// <summary>In possession with the ball in its own part of the pitch: spread, the back line patient.</summary>
    BuildUp = 0,

    /// <summary>In possession further up: the lines move up behind the ball.</summary>
    Attack = 1,

    /// <summary>Out of possession with the ball close to its goal: a back line on the edge of the box, midfield tight in front.</summary>
    LowBlock = 2,

    /// <summary>Out of possession in the middle of the pitch: three compact lines.</summary>
    MidBlock = 3,

    /// <summary>Out of possession, asked to press, with the ball in the other side's half: the lines are pushed up.</summary>
    HighPress = 4,
}

/// <summary>
/// The places a cross sets the box at (`replay-v6`): where the attackers run to, and where the defenders stand to mark
/// them or to hold a zone.
/// </summary>
/// <param name="NearPost">The attacker who runs at the near post.</param>
/// <param name="FarPost">The attacker who runs at the far post.</param>
/// <param name="PenaltySpot">The attacker who arrives at the penalty spot.</param>
/// <param name="Cutback">The attacker who waits at the edge of the box for a ball pulled back.</param>
/// <param name="NearMark">The defender who marks the near-post runner, goal-side of him.</param>
/// <param name="FarMark">The defender who marks the far-post runner.</param>
/// <param name="SpotMark">The defender who marks the runner at the penalty spot.</param>
/// <param name="CutbackMark">The defender at the edge of the box who picks up the cutback.</param>
/// <param name="SixYardZone">The defender who holds the middle of the six-yard box.</param>
/// <param name="BoxZone">The defender who holds the ball side of the penalty area.</param>
internal readonly record struct BoxSet(
    Vec NearPost,
    Vec FarPost,
    Vec PenaltySpot,
    Vec Cutback,
    Vec NearMark,
    Vec FarMark,
    Vec SpotMark,
    Vec CutbackMark,
    Vec SixYardZone,
    Vec BoxZone);

/// <summary>
/// Where the twenty-two players want to be (`replay-v4`, lines and phases `replay-v6`).
/// </summary>
/// <remarks>
/// <para>
/// The shape starts from the tactics board: each slot's anchor is <see cref="TacticalFormationResolver.Orient"/>
/// as the resolver gives it, moved by the side's real instructions — mentality, defensive line, width, and the
/// press — through the same resolver the simulation uses, with the ball held at the centre so the ball's own pull
/// can be applied here instead. A set piece is then arranged over the top of it: a corner and a free kick delivered into
/// the box by role with runners and markers, a free kick struck at goal with a wall sized by the distance, a penalty
/// with everybody out of the box and the arc, and a goal kick with one side spread and the other stepped up.
/// </para>
/// <para>
/// A side then stands as three lines — defence, midfield, attack — each at the depth its anchors give, moved a
/// little way along the pitch and a good way across it towards the ball. What it does with the lines depends on its
/// <see cref="BlockPhase"/>: the lines close up out of possession, a low block keeps the back line at the edge of
/// its box with the forwards left high as an outlet, a high press pushes the lot up, and in possession the lines
/// stay spread. A last pass keeps any two players a few metres apart, so a block is never a clump. The
/// goalkeeper stands on the line between the ball and the goal.
/// </para>
/// <para>
/// The resolver itself is left alone, because the spatial play model's tests pin it. Set pieces and the
/// celebration override the shape for the players they involve.
/// </para>
/// </remarks>
internal sealed class FilmShape
{
    /// <summary>How far along the pitch the block follows the ball from the centre spot.</summary>
    private const double AlongPull = 0.15;

    /// <summary>How far across the pitch the block follows the ball from the centre line.</summary>
    private const double AcrossPull = 0.30;

    /// <summary>How far inside the lines an outfield player is kept.</summary>
    private const double LineMargin = 1.5;

    /// <summary>The centre circle's radius, in metres.</summary>
    private const double CircleRadius = 9.15;

    /// <summary>How near its goal the ball is, in metres, before a side in possession is building up.</summary>
    private const double BuildUpDepth = 35.0;

    /// <summary>How near its goal the ball is before a mid-block side drops into a low block.</summary>
    private const double LowBlockBall = 38.0;

    /// <summary>How near its goal the ball is before a side told to defend deep drops into a low block.</summary>
    private const double DeepBlockBall = 55.0;

    /// <summary>How far from its goal the ball is before a side told to press does so.</summary>
    private const double HighPressBall = 55.0;

    /// <summary>The deepest a back line stands in a low block: the edge of the penalty area, in metres from the goal line.</summary>
    private const double LowBlockBackLine = 18.5;

    /// <summary>How far in front of the back line a low block's midfield stands.</summary>
    private const double LowBlockMidfieldGap = 10.5;

    /// <summary>How far in front of its midfield a low block leaves its forwards, at the least.</summary>
    private const double LowBlockForwardGap = 12.0;

    /// <summary>The furthest up a low block leaves its forwards: they stay in the other half as an outlet.</summary>
    private const double LowBlockForwardCap = 50.0;

    /// <summary>How many lines a side can stand in at most.</summary>
    private const int MaxLines = 5;

    /// <summary>How much deeper than the player before one may be and still stand in the same line, in metres.</summary>
    private const double SameLine = 9.0;

    /// <summary>The least that separates two lines, in metres.</summary>
    private const double MinLineGap = 8.0;

    /// <summary>How near any two outfield players' targets may be, in metres.</summary>
    private const double MinSpacing = 3.0;

    /// <summary>How many times the separation pass sweeps the players.</summary>
    private const int SeparationSweeps = 6;

    /// <summary>How much more the players on the far side from the ball close in.</summary>
    private const double FarSideTuck = 0.90;

    /// <summary>How far short of the place he is running to a corner's runner waits, in metres (`replay-v6`).</summary>
    private const double CornerRunUp = 5.0;

    /// <summary>How far goal-side of the runner his marker stands, in metres (`replay-v6`).</summary>
    private const double CornerMarkGap = 1.4;

    /// <summary>How much a point of heading is worth against a metre of how far forward a player plays, when choosing a corner's runners (`replay-v6`).</summary>
    private const double CornerForwardWeight = 0.15;

    /// <summary>How many defenders a side needs before it leaves one up the pitch as an outlet at a corner (`replay-v6`).</summary>
    private const int CornerOutletFrom = 5;

    /// <summary>How far from the ball the defence stands at a free kick, in metres: a little over the 9.15 the rules ask for (`replay-v6`).</summary>
    private const double FreeKickGap = 9.5;

    /// <summary>How far apart the players of a wall stand, shoulder to shoulder, in metres (`replay-v6`).</summary>
    private const double WallSpacing = 0.75;

    /// <summary>How far outside the middle of the goal the wall's line, and the keeper's cover, reach towards a post, in metres (`replay-v6`).</summary>
    private const double FreeKickPostReach = 1.8;

    /// <summary>How far from the goal line the attackers wait for a rebound, in metres: the edge of the penalty area (`replay-v6`).</summary>
    private const double FreeKickRebound = 20.5;

    /// <summary>How deep the penalty area is, in metres.</summary>
    private const double PenaltyBoxDepth = 16.5;

    /// <summary>How far to each side of the middle of the goal the penalty area reaches, in metres.</summary>
    private const double PenaltyBoxHalfWidth = 20.16;

    /// <summary>How many players stand in one row at a penalty at the most (`replay-v6`).</summary>
    private const int PenaltyRowSize = 9;

    /// <summary>How far apart the players of a penalty's rows stand, along the row, in metres (`replay-v6`).</summary>
    private const double PenaltySpacing = 4.0;

    /// <summary>How far one row of a penalty is from the next, in metres (`replay-v6`).</summary>
    private const double PenaltyRowGap = 3.0;

    // A goal kick, as metres from the side's own goal line and metres either side of the middle of the pitch: the side taking
    // it spreads out, its back line at the edge of its box; the side receiving it steps up to the halfway line (`replay-v6`).
    private const double GoalKickBack = 18.0;
    private const double GoalKickFront = 56.0;
    private const double GoalKickBackHalfWidth = 26.0;
    private const double GoalKickFrontHalfWidth = 18.0;
    private const double StepUpBack = 47.5;
    private const double StepUpFront = 64.0;
    private const double StepUpBackHalfWidth = 20.0;
    private const double StepUpFrontHalfWidth = 14.0;

    // A free kick delivered into the box, as the corner's are: the places the runners run to, the line the rest of the
    // defence makes at the edge of the box, and the one attacker who waits there for the second ball.
    private static readonly (double Along, double Across)[] FreeKickRunners = [(11.0, -0.5), (7.5, -5.0), (8.5, 4.0), (14.0, 7.5), (14.5, -9.0), (10.0, 10.0)];
    private static readonly (double Along, double Across)[] FreeKickLine = [(18.5, -10.5), (19.0, 0.5), (18.5, 10.5)];
    private static readonly (double Along, double Across) FreeKickEdge = (22.0, 7.0);

    // The places a corner is set at, as metres from the goal line and metres across from the middle of the pitch, where
    // positive is towards the flag the corner is taken from. The runners are in order of who gets which: the best header
    // takes the penalty spot.
    private static readonly (double Along, double Across)[] CornerRunners = [(10.5, -0.5), (6.0, -4.5), (6.0, 3.0), (9.0, 6.5)];
    private static readonly (double Along, double Across)[] CornerEdge = [(18.5, 8.0), (19.0, -7.0)];
    private static readonly (double Along, double Across)[] CornerGuards = [(51.0, -9.0), (51.0, 9.0), (57.0, 0.0)];
    private static readonly (double Along, double Across)[] CornerPosts = [(1.4, 3.0), (1.4, -3.0)];
    private static readonly (double Along, double Across)[] CornerZones = [(5.0, 6.5), (5.0, -0.8), (5.5, -8.0)];
    private static readonly (double Along, double Across) CornerDefendersEdge = (17.0, 1.0);
    private static readonly (double Along, double Across) CornerOutlet = (46.0, -12.0);

    private readonly FilmContext _context;

    // Indexed [entity][0 = out of possession, 1 = in possession]; the anchors do not depend on the ball.
    private readonly Vec[,] _anchors = new Vec[FilmRoster.Size, 2];

    // Indexed [side][phase][line], the mean depth from the side's own goal of the outfield players in the line, the
    // back line first; [entity][phase] says which line a player stands in, and [side][phase] how many lines there are.
    private readonly double[,,] _lineDepth = new double[2, 2, MaxLines];
    private readonly int[,] _lineOf = new int[FilmRoster.Size, 2];
    private readonly int[,] _lineCount = new int[2, 2];

    // Indexed [side][phase], the mean width of the outfield players.
    private readonly double[,] _across = new double[2, 2];

    /// <summary>Initializes the shape from the frozen snapshot.</summary>
    /// <param name="context">The film's context.</param>
    public FilmShape(FilmContext context)
    {
        _context = context;

        var centre = new SpatialPoint(SpatialPitch.PitchLength / 2, SpatialPitch.PitchWidth / 2);

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            var side = FilmRoster.SideOf(entity);

            for (var phase = 0; phase < 2; phase++)
            {
                var resolved = TacticalFormationResolver.ResolvePosition(
                    context.Slots[entity],
                    side == MatchSide.Home,
                    phase == 1,
                    centre,
                    context.InstructionsOf(side),
                    context.Rules);

                _anchors[entity, phase] = FilmSpace.FromEngine(resolved.X, resolved.Y);
            }
        }

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            for (var phase = 0; phase < 2; phase++)
            {
                var outfield = new List<int>(10);
                double width = 0;

                for (var slot = 1; slot <= 11; slot++)
                {
                    var entity = FilmRoster.Index(side, slot);

                    if (context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
                    {
                        continue;
                    }

                    outfield.Add(entity);
                    width += _anchors[entity, phase].Y;
                }

                _across[(int)side, phase] = outfield.Count == 0 ? FilmSpace.Width / 2 : width / outfield.Count;
                FormLines(side, phase, outfield);
            }
        }
    }

    /// <summary>
    /// Groups a side's outfield players into lines by how deep their anchors stand: players within a few metres of the
    /// next one in depth are the same line, whatever family their slot says, so a winger stands with the midfield.
    /// </summary>
    private void FormLines(MatchSide side, int phase, List<int> outfield)
    {
        var ordered = outfield
            .OrderBy(entity => FilmSpace.Attacking(_anchors[entity, phase], side))
            .ThenBy(entity => entity)
            .ToArray();

        var line = -1;
        var previous = double.MinValue;
        var sum = 0.0;
        var members = 0;

        foreach (var entity in ordered)
        {
            var depth = FilmSpace.Attacking(_anchors[entity, phase], side);

            if (line < 0 || (depth - previous > SameLine && line < MaxLines - 1))
            {
                if (line >= 0)
                {
                    _lineDepth[(int)side, phase, line] = sum / members;
                }

                line++;
                sum = 0;
                members = 0;
            }

            _lineOf[entity, phase] = line;
            sum += depth;
            members++;
            previous = depth;
        }

        if (line >= 0)
        {
            _lineDepth[(int)side, phase, line] = sum / members;
        }

        _lineCount[(int)side, phase] = line + 1;
    }

    /// <summary>Fills in where every occupied entity wants to be.</summary>
    /// <param name="roster">Who is on the pitch.</param>
    /// <param name="state">What the shape is arranged around.</param>
    /// <param name="focus">The point the block follows: the ball, smoothed.</param>
    /// <param name="targets">The twenty-two targets, written in place.</param>
    public void Fill(FilmRoster roster, ShapeState state, Vec focus, Vec[] targets)
    {
        switch (state.Mode)
        {
            case FormationMode.KickOff:
                FillOpen(roster, state.Side, FilmSpace.Centre, targets, separate: false);
                ArrangeKickOff(roster, state, targets);
                break;

            case FormationMode.Corner:
                FillOpen(roster, state.Side, FilmSpace.Centre, targets, separate: false);
                ArrangeCorner(roster, state, targets);
                break;

            case FormationMode.FreeKickShot:
                FillOpen(roster, state.Side, state.Anchor, targets, separate: false);
                ArrangeFreeKickShot(roster, state, targets);
                break;

            case FormationMode.FreeKickCross:
                FillOpen(roster, state.Side, state.Anchor, targets, separate: false);
                ArrangeFreeKickCross(roster, state, targets);
                break;

            case FormationMode.FreeKickQuick:
                FillOpen(roster, state.Side, state.Anchor, targets);
                PlaceTaker(roster, state, targets);
                KeepOff(roster, MatchInputV1.OpponentOf(state.Side), state.Anchor, targets);
                break;

            case FormationMode.Penalty:
                FillOpen(roster, state.Side, FilmSpace.Centre, targets, separate: false);
                ArrangePenalty(roster, state, targets);
                break;

            case FormationMode.GoalKick:
                FillOpen(roster, state.Side, state.Anchor, targets, separate: false);
                ArrangeGoalKick(roster, state, targets);
                break;

            case FormationMode.Celebration:
                FillOpen(roster, state.Side, state.Anchor, targets, separate: false);
                ArrangeCelebration(roster, state, targets);
                break;

            default:
                FillOpen(roster, state.Side, focus, targets);
                break;
        }
    }

    /// <summary>Gets the line an outfield player stands in: 0 is the back line, the last is the front line (`replay-v6`).</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="inPossession">Whether the player's side has the ball, which is the anchors the lines are cut from.</param>
    public int LineOf(int entity, bool inPossession) => _lineOf[entity, inPossession ? 1 : 0];

    /// <summary>Gets how many lines a side stands in (`replay-v6`).</summary>
    /// <param name="side">The side.</param>
    /// <param name="inPossession">Whether the side has the ball.</param>
    public int LineCountOf(MatchSide side, bool inPossession) => _lineCount[(int)side, inPossession ? 1 : 0];

    /// <summary>Gets the phase a side is in, from who has the ball and how far it is from the side's own goal.</summary>
    /// <param name="side">The side.</param>
    /// <param name="possession">The side with the ball.</param>
    /// <param name="ballDepth">Metres from the side's own goal line to the ball.</param>
    public BlockPhase PhaseOf(MatchSide side, MatchSide possession, double ballDepth)
    {
        if (side == possession)
        {
            return ballDepth < BuildUpDepth ? BlockPhase.BuildUp : BlockPhase.Attack;
        }

        var pressing = _context.InstructionsOf(side).Pressing;

        if (ballDepth < (pressing == MatchPressing.LowBlock ? DeepBlockBall : LowBlockBall))
        {
            return BlockPhase.LowBlock;
        }

        return pressing == MatchPressing.HighPress && ballDepth >= HighPressBall
            ? BlockPhase.HighPress
            : BlockPhase.MidBlock;
    }

    /// <summary>Fills the open-play shape: each side stands as lines for its phase around the focus, with the keepers on their lines.</summary>
    /// <param name="roster">Who is on the pitch.</param>
    /// <param name="possession">The side with the ball.</param>
    /// <param name="focus">The point the block follows.</param>
    /// <param name="targets">The targets, written in place.</param>
    /// <param name="separate">Whether to keep any two outfield players apart; set pieces place their own.</param>
    public void FillOpen(FilmRoster roster, MatchSide possession, Vec focus, Vec[] targets, bool separate = true)
    {
        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            FillSide(roster, side, possession, focus, targets);
        }

        if (separate)
        {
            Separate(roster, targets);
        }
    }

    private void FillSide(FilmRoster roster, MatchSide side, MatchSide possession, Vec focus, Vec[] targets)
    {
        var phaseIndex = side == possession ? 1 : 0;
        var ballDepth = FilmSpace.Attacking(focus, side);
        var phase = PhaseOf(side, possession, ballDepth);
        var (gap, pivot, across, stagger) = Settings(phase);

        // The depth of each line from the side's own goal: where its anchors put it, moved a little with the ball and
        // closed up about the middle of the block.
        var along = AlongPull * (ballDepth - (FilmSpace.Length / 2));
        var count = _lineCount[(int)side, phaseIndex];
        Span<double> lines = stackalloc double[MaxLines];
        var middle = 0.0;

        for (var line = 0; line < count; line++)
        {
            middle += _lineDepth[(int)side, phaseIndex, line] + along;
        }

        middle /= Math.Max(1, count);

        // The lines close up about a point between the middle of the block and its back line.
        var about = middle + ((_lineDepth[(int)side, phaseIndex, 0] + along - middle) * pivot);

        for (var line = 0; line < count; line++)
        {
            lines[line] = about + ((_lineDepth[(int)side, phaseIndex, line] + along - about) * gap);
        }

        if (phase == BlockPhase.LowBlock && count >= 2)
        {
            // The back line stops at the edge of the box, the midfield stands tight in front of it, and the forwards
            // are left up the pitch as an outlet.
            lines[0] = Math.Max(lines[0], LowBlockBackLine);

            for (var line = 1; line < count - 1; line++)
            {
                lines[line] = lines[line - 1] + (line == 1 ? LowBlockMidfieldGap : MinLineGap + 1.0);
            }

            var last = count - 1;
            var forwards = Math.Max(lines[last], lines[last - 1] + (last == 1 ? LowBlockMidfieldGap : LowBlockForwardGap));

            lines[last] = Math.Min(forwards, Math.Max(LowBlockForwardCap, lines[last - 1] + MinLineGap));
        }
        else
        {
            for (var line = 1; line < count; line++)
            {
                lines[line] = Math.Max(lines[line], lines[line - 1] + MinLineGap);
            }
        }

        var centreAcross = _across[(int)side, phaseIndex];
        var pull = AcrossPull * (focus.Y - (FilmSpace.Width / 2));
        var ballSide = Math.Sign(focus.Y - centreAcross);

        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(side, slot);

            if (!roster.IsOccupied(entity))
            {
                continue;
            }

            if (_context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                targets[entity] = KeeperOn(side, focus);

                continue;
            }

            var anchor = _anchors[entity, phaseIndex];
            var line = _lineOf[entity, phaseIndex];
            var inLine = (FilmSpace.Attacking(anchor, side) - _lineDepth[(int)side, phaseIndex, line]) * stagger;
            var depth = lines[line] + inLine;

            // The players on the far side from the ball tuck in a little more than the ones near it.
            var offset = anchor.Y - centreAcross;
            var tuck = ballSide != 0 && Math.Sign(offset) == -ballSide ? FarSideTuck : 1.0;

            targets[entity] = FilmSpace.Clamp(
                new Vec(
                    side == MatchSide.Home ? depth : FilmSpace.Length - depth,
                    centreAcross + (offset * across * tuck) + pull),
                LineMargin);
        }
    }

    /// <summary>
    /// How a phase stands: how tightly its lines close (1 keeps the anchors' gaps), how far towards the back line the
    /// closing is about (0 is the middle of the block, 1 the back line, so the back line holds), how wide it is, and
    /// how much of a line's own stagger is kept.
    /// </summary>
    private static (double Gap, double Pivot, double Across, double Stagger) Settings(BlockPhase phase) => phase switch
    {
        BlockPhase.BuildUp => (1.00, 0.0, 1.05, 1.0),
        BlockPhase.Attack => (1.00, 0.0, 1.08, 1.0),
        BlockPhase.LowBlock => (1.00, 0.0, 0.85, 0.6),
        BlockPhase.MidBlock => (0.55, 0.5, 0.88, 0.9),
        _ => (0.60, 0.0, 0.88, 0.9),
    };

    /// <summary>Pushes apart any two outfield players whose targets are closer than <see cref="MinSpacing"/>.</summary>
    private void Separate(FilmRoster roster, Vec[] targets)
    {
        Span<int> players = stackalloc int[FilmRoster.Size];
        var count = 0;

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (roster.IsOccupied(entity) && _context.Slots[entity].Family != MatchPositionFamily.Goalkeeper)
            {
                players[count++] = entity;
            }
        }

        for (var sweep = 0; sweep < SeparationSweeps; sweep++)
        {
            var moved = false;

            for (var first = 0; first < count; first++)
            {
                for (var second = first + 1; second < count; second++)
                {
                    var a = players[first];
                    var b = players[second];
                    var between = targets[b] - targets[a];
                    var distance = between.Length;

                    if (distance >= MinSpacing)
                    {
                        continue;
                    }

                    // Two on the same spot part along the pitch's width, the lower entity to the low side.
                    var direction = distance < 1e-6 ? new Vec(0, 1) : new Vec(between.X / distance, between.Y / distance);
                    var push = direction * ((MinSpacing - distance) / 2.0);

                    targets[a] = FilmSpace.Clamp(targets[a] - push, LineMargin);
                    targets[b] = FilmSpace.Clamp(targets[b] + push, LineMargin);
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }
    }

    /// <summary>Gets where a goalkeeper stands: on the line between the ball and the goal, further out the further the ball is.</summary>
    /// <param name="side">The goalkeeper's side.</param>
    /// <param name="ball">The ball.</param>
    public static Vec KeeperOn(MatchSide side, Vec ball)
    {
        var goal = FilmSpace.OwnGoal(side);
        var toBall = ball - goal;
        var distance = toBall.Length;
        var off = double.Clamp(1.0 + (0.06 * distance), 1.5, 8.0);

        return FilmSpace.Clamp(goal + (toBall.Unit() * off), 0.5);
    }

    /// <summary>Gets the point a player with the given attacking-frame coordinates stands at.</summary>
    /// <param name="side">The side attacking.</param>
    /// <param name="alongFromGoalLine">Metres from the goal line the side attacks.</param>
    /// <param name="acrossFromCentre">Metres across from the centre line; positive is up the Y axis.</param>
    public static Vec Frame(MatchSide side, double alongFromGoalLine, double acrossFromCentre) =>
        new(
            side == MatchSide.Home ? FilmSpace.Length - alongFromGoalLine : alongFromGoalLine,
            (FilmSpace.Width / 2) + acrossFromCentre);

    /// <summary>
    /// Gets where the box is set for a cross from one flank (`replay-v6`): a staggered row of runners, each with a
    /// defender goal-side of him, and two more defenders who hold zones. The runners and their markers are paired by
    /// whoever is nearest, not by slot, so only the places are fixed here.
    /// </summary>
    /// <param name="attacking">The side crossing the ball.</param>
    /// <param name="ballSide">Which way the ball comes from across the pitch: positive towards the high-Y touchline, negative towards the other.</param>
    public static BoxSet SetBox(MatchSide attacking, double ballSide)
    {
        var sign = ballSide >= 0 ? 1.0 : -1.0;

        Vec At(double along, double across) => Frame(attacking, along, across * sign);

        return new BoxSet(
            NearPost: At(5.0, 4.5),
            FarPost: At(6.0, -5.0),
            PenaltySpot: At(11.0, -0.5),
            Cutback: At(18.0, 7.0),
            NearMark: At(3.7, 3.6),
            FarMark: At(4.7, -4.0),
            SpotMark: At(9.7, -0.4),
            CutbackMark: At(15.5, 6.0),
            SixYardZone: At(3.0, 0.4),
            BoxZone: At(8.5, 7.5));
    }

    private static void ArrangeKickOff(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var centre = FilmSpace.Centre;

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (!roster.IsOccupied(entity))
            {
                continue;
            }

            var side = FilmRoster.SideOf(entity);
            var direction = FilmSpace.Direction(side);
            var point = targets[entity];

            // Each side starts in its own half: the home side behind the halfway line on the low side, the away
            // side on the high side.
            var limit = (FilmSpace.Length / 2) - (direction * 1.0);

            point = side == MatchSide.Home
                ? new Vec(Math.Min(point.X, limit), point.Y)
                : new Vec(Math.Max(point.X, limit), point.Y);

            // The side not kicking off keeps out of the centre circle.
            if (side != state.Side && point.DistanceTo(centre) < CircleRadius + 0.5)
            {
                var outward = (point - centre).Unit();

                if (outward == Vec.Zero)
                {
                    outward = new Vec(-direction, 0);
                }

                point = centre + (outward * (CircleRadius + 0.5));

                point = side == MatchSide.Home
                    ? new Vec(Math.Min(point.X, limit), point.Y)
                    : new Vec(Math.Max(point.X, limit), point.Y);
            }

            targets[entity] = FilmSpace.Clamp(point, LineMargin);
        }
    }

    /// <summary>
    /// Sets a corner by role (`replay-v6`), mirrored for the flag it is taken from. The attackers are the taker, the best
    /// headers in the box, a pair at the edge for the second ball and two or three held back near the halfway line
    /// against a break; the defenders are two on the posts, three holding the six-yard line, markers for the runners,
    /// one at the edge and one left high as an outlet. The runners wait a few metres short of the places they are
    /// running to, and the markers stay goal-side of them; the places themselves do not move with the ball.
    /// </summary>
    private void ArrangeCorner(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var attacking = state.Side;
        var defending = MatchInputV1.OpponentOf(attacking);
        var flag = state.Anchor.Y >= FilmSpace.Width / 2 ? 1.0 : -1.0;

        Vec At((double Along, double Across) place, double back = 0.0) => Frame(attacking, place.Along + back, place.Across * flag);

        var taker = state.Taker >= 0 && roster.IsOccupied(state.Taker) && FilmRoster.SideOf(state.Taker) == attacking ? state.Taker : -1;
        var pool = Order(roster, attacking, MatchPositionFamily.Attack).Where(entity => entity != taker).ToList();

        if (taker >= 0)
        {
            targets[taker] = state.Anchor;
        }

        // Who does what: two or three guard against the break, the best headers go into the box, the rest wait at its edge.
        var guards = Math.Min(pool.Count >= 8 ? 3 : 2, pool.Count);
        var rest = pool.Count - guards;
        var edge = rest >= 5 ? 2 : (rest >= 3 ? 1 : 0);
        var box = Math.Min(CornerRunners.Length, rest - edge);

        var guarding = ChooseGuards(pool, guards, attacking);

        pool.RemoveAll(guarding.Contains);

        for (var place = 0; place < CornerGuards.Length && guarding.Count > 0; place++)
        {
            var point = At(CornerGuards[place]);
            var who = TakeNearest(guarding, point, targets);

            targets[who] = point;
        }

        // The best headers go in, but one who plays far back cannot get there in time: how far forward he plays counts too.
        var runners = pool
            .OrderByDescending(entity => _context.AttributeOf(roster.Occupants[entity], MatchAttributeName.Heading)
                + (CornerForwardWeight * FilmSpace.Attacking(_anchors[entity, 1], attacking)))
            .ThenBy(entity => entity)
            .Take(box)
            .ToList();

        pool.RemoveAll(runners.Contains);

        var run = state.Arrived ? 0.0 : CornerRunUp;

        for (var index = 0; index < runners.Count; index++)
        {
            targets[runners[index]] = At(CornerRunners[index], run);
        }

        for (var place = 0; place < CornerEdge.Length && pool.Count > 0; place++)
        {
            var point = At(CornerEdge[place]);
            var who = TakeNearest(pool, point, targets);

            targets[who] = point;
        }

        // The defenders are chosen for the places the runners come to, so that who marks whom does not change as they run
        // in; where each stands is where his runner is now.
        var defenders = Order(roster, defending, MatchPositionFamily.Defence).ToList();

        if (defenders.Count >= CornerOutletFrom)
        {
            var outlet = defenders
                .OrderByDescending(entity => FilmSpace.Attacking(_anchors[entity, 0], defending))
                .ThenBy(entity => entity)
                .First();

            defenders.Remove(outlet);
            targets[outlet] = At(CornerOutlet);
        }

        var jobs = new List<(Vec Place, Vec Stands)>(CornerPosts.Length + CornerZones.Length + CornerRunners.Length + 1);

        (Vec, Vec) Marking(int runner)
        {
            var place = CornerRunners[runner];

            return (At((place.Along - CornerMarkGap, place.Across)), At((place.Along - CornerMarkGap, place.Across), run));
        }

        foreach (var post in CornerPosts)
        {
            jobs.Add((At(post), At(post)));
        }

        foreach (var zone in CornerZones)
        {
            jobs.Add((At(zone), At(zone)));
        }

        // Fewer markers than runners: the best header is picked up first.
        for (var runner = 0; runner < Math.Min(2, runners.Count); runner++)
        {
            jobs.Add(Marking(runner));
        }

        jobs.Add((At(CornerDefendersEdge), At(CornerDefendersEdge)));

        for (var runner = 2; runner < runners.Count; runner++)
        {
            jobs.Add(Marking(runner));
        }

        foreach (var (place, stands) in jobs)
        {
            if (defenders.Count == 0)
            {
                break;
            }

            targets[TakeNearest(defenders, place, targets)] = stands;
        }

        KeepKeeper(roster, defending, targets, 1.2);
    }

    /// <summary>Chooses who stays back against a break: the most central defenders, then the deepest midfielder, then the deepest left.</summary>
    private List<int> ChooseGuards(List<int> pool, int count, MatchSide side)
    {
        var chosen = new List<int>(count);
        var middle = FilmSpace.Width / 2;

        chosen.AddRange(
            pool.Where(entity => _context.Slots[entity].Family == MatchPositionFamily.Defence)
                .OrderBy(entity => Math.Abs(_anchors[entity, 1].Y - middle))
                .ThenBy(entity => entity)
                .Take(Math.Min(2, count)));

        foreach (var family in new[] { MatchPositionFamily.Midfield, MatchPositionFamily.Attack, MatchPositionFamily.Defence })
        {
            if (chosen.Count >= count)
            {
                break;
            }

            chosen.AddRange(
                pool.Where(entity => !chosen.Contains(entity) && _context.Slots[entity].Family == family)
                    .OrderBy(entity => FilmSpace.Attacking(_anchors[entity, 1], side))
                    .ThenBy(entity => entity)
                    .Take(count - chosen.Count));
        }

        return chosen;
    }

    /// <summary>Takes out of a pool the player whose place in the shape is nearest a point.</summary>
    private static int TakeNearest(List<int> pool, Vec point, Vec[] targets)
    {
        var best = 0;

        for (var index = 1; index < pool.Count; index++)
        {
            if (targets[pool[index]].DistanceTo(point) < targets[pool[best]].DistanceTo(point) - 1e-9)
            {
                best = index;
            }
        }

        var entity = pool[best];

        pool.RemoveAt(best);

        return entity;
    }

    /// <summary>Gets how many defenders stand in the wall for a free kick struck from a distance from goal, in metres (`replay-v6`).</summary>
    /// <param name="distance">How far the ball is from the middle of the goal.</param>
    internal static int WallSize(double distance) => distance switch
    {
        < 20.0 => 5,
        < 25.0 => 4,
        < 30.0 => 3,
        _ => 2,
    };

    /// <summary>
    /// Sets a free kick struck at goal (`replay-v6`): a wall of two to five by the distance, on the line to the near
    /// post; the keeper covering the far side; three attackers at the edge of the box for the rebound, and the rest of
    /// the attack held outside it.
    /// </summary>
    private void ArrangeFreeKickShot(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var attacking = state.Side;
        var defending = MatchInputV1.OpponentOf(attacking);
        var spot = state.Anchor;
        var goal = FilmSpace.AttackedGoal(attacking);
        var taker = PlaceTaker(roster, state, targets);

        var offset = spot.Y - goal.Y;
        var nearPost = Math.Abs(offset) < 1.0 ? 0.0 : Math.Sign(offset);
        var toPost = (new Vec(goal.X, goal.Y + (nearPost * FreeKickPostReach)) - spot).Unit();
        var across = new Vec(-toPost.Y, toPost.X);

        var wall = Order(roster, defending, MatchPositionFamily.Defence)
            .Where(entity => _context.Slots[entity].Family != MatchPositionFamily.Attack)
            .OrderBy(entity => targets[entity].DistanceTo(spot))
            .ThenBy(entity => entity)
            .Take(WallSize(spot.DistanceTo(goal)))
            .OrderBy(entity => (targets[entity].X * across.X) + (targets[entity].Y * across.Y))
            .ToArray();

        // The rest of the defence stands where its own shape puts it, the wall's ten yards from the ball apart.
        KeepOff(roster, defending, spot, targets);

        for (var index = 0; index < wall.Length; index++)
        {
            var step = (index - ((wall.Length - 1) / 2.0)) * WallSpacing;

            targets[wall[index]] = FilmSpace.Clamp(spot + (toPost * FreeKickGap) + (across * step), LineMargin);
        }

        var pool = Order(roster, attacking, MatchPositionFamily.Attack).Where(entity => entity != taker).ToList();
        var waiting = Math.Min(pool.Count >= 8 ? 3 : 2, pool.Count);

        for (var index = 0; index < pool.Count; index++)
        {
            targets[pool[index]] = index < waiting
                ? AwayFrom(Frame(attacking, FreeKickRebound, (index - ((waiting - 1) / 2.0)) * 8.0), spot, 2.5)
                : OutsideBox(attacking, targets[pool[index]]);
        }

        // The keeper covers the side the wall does not.
        KeepKeeper(roster, defending, targets, 1.0, -nearPost * FreeKickPostReach);
    }

    /// <summary>
    /// Sets a free kick delivered into the box (`replay-v6`): no wall. The runners are the best headers, waiting a few
    /// metres short of their places until the ball is struck, each with a marker goal-side of him; the rest of the
    /// defence is a line at the edge of the box with one player left up the pitch; one attacker is at the edge for
    /// the second ball and a pair hold back against a break.
    /// </summary>
    private void ArrangeFreeKickCross(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var attacking = state.Side;
        var defending = MatchInputV1.OpponentOf(attacking);
        var spot = state.Anchor;
        var flank = spot.Y >= FilmSpace.Width / 2 ? 1.0 : -1.0;
        var run = state.Arrived ? 0.0 : CornerRunUp;

        Vec At((double Along, double Across) place, double back = 0.0) => Frame(attacking, place.Along + back, place.Across * flank);

        var taker = PlaceTaker(roster, state, targets);
        var pool = Order(roster, attacking, MatchPositionFamily.Attack).Where(entity => entity != taker).ToList();
        var guards = Math.Min(pool.Count >= 8 ? 2 : 1, pool.Count);
        var guarding = ChooseGuards(pool, guards, attacking);

        pool.RemoveAll(guarding.Contains);

        for (var place = 0; place < guards; place++)
        {
            var point = At(CornerGuards[place]);

            targets[TakeNearest(guarding, point, targets)] = point;
        }

        var edge = pool.Count > 4 ? 1 : 0;

        var runners = pool
            .OrderByDescending(entity => _context.AttributeOf(roster.Occupants[entity], MatchAttributeName.Heading)
                + (CornerForwardWeight * FilmSpace.Attacking(_anchors[entity, 1], attacking)))
            .ThenBy(entity => entity)
            .Take(Math.Min(FreeKickRunners.Length, pool.Count - edge))
            .ToList();

        pool.RemoveAll(runners.Contains);

        for (var index = 0; index < runners.Count; index++)
        {
            targets[runners[index]] = At(FreeKickRunners[index], run);
        }

        foreach (var entity in pool)
        {
            targets[entity] = edge > 0 ? At(FreeKickEdge) : OutsideBox(attacking, targets[entity]);
            edge = 0;
        }

        // Each runner is picked up goal-side of where he runs to, so that who marks whom does not change as they run in;
        // the others make the line at the edge of the box.
        var defenders = Order(roster, defending, MatchPositionFamily.Defence).ToList();

        if (defenders.Count >= CornerOutletFrom)
        {
            var outlet = defenders
                .OrderByDescending(entity => FilmSpace.Attacking(_anchors[entity, 0], defending))
                .ThenBy(entity => entity)
                .First();

            defenders.Remove(outlet);
            targets[outlet] = At(CornerOutlet);
        }

        var jobs = new List<(Vec Place, Vec Stands)>(FreeKickRunners.Length + FreeKickLine.Length);

        for (var runner = 0; runner < runners.Count; runner++)
        {
            var place = FreeKickRunners[runner];

            jobs.Add((At((place.Along - CornerMarkGap, place.Across)), At((place.Along - CornerMarkGap, place.Across), run)));
        }

        foreach (var place in FreeKickLine)
        {
            jobs.Add((At(place), At(place)));
        }

        foreach (var (place, stands) in jobs)
        {
            if (defenders.Count == 0)
            {
                break;
            }

            targets[TakeNearest(defenders, place, targets)] = stands;
        }

        KeepKeeper(roster, defending, targets, 1.2);
        KeepOff(roster, defending, spot, targets);
    }

    /// <summary>
    /// Sets a penalty (`replay-v6`): everybody but the taker and the keeper stands outside the box and the arc, in rows
    /// of seven at the most, the two sides alternating along each row so that neither is stacked behind the other.
    /// </summary>
    private void ArrangePenalty(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var attacking = state.Side;
        var defending = MatchInputV1.OpponentOf(attacking);
        var taker = PlaceTaker(roster, state, targets);
        var spotAlong = attacking == MatchSide.Home ? FilmSpace.Length - state.Anchor.X : state.Anchor.X;
        var first = Math.Max(spotAlong + FreeKickGap + 0.5, PenaltyBoxDepth + 1.5);

        var shooters = Order(roster, attacking, MatchPositionFamily.Attack).Where(entity => entity != taker).ToArray();
        var markers = Order(roster, defending, MatchPositionFamily.Defence);
        var queue = new List<int>(shooters.Length + markers.Length);

        // The side with more players leads, so that the two alternate to the very end instead of one ending in a pair.
        var (lead, follow) = markers.Length > shooters.Length ? (markers, shooters) : (shooters, markers);

        for (var index = 0; index < lead.Length; index++)
        {
            queue.Add(lead[index]);

            if (index < follow.Length)
            {
                queue.Add(follow[index]);
            }
        }

        var rows = Math.Max(1, (int)Math.Ceiling(queue.Count / (double)PenaltyRowSize));
        var perRow = (int)Math.Ceiling(queue.Count / (double)rows);

        for (var index = 0; index < queue.Count; index++)
        {
            var row = index / perRow;
            var inRow = Math.Min(perRow, queue.Count - (row * perRow));
            var across = (((index % perRow) - ((inRow - 1) / 2.0)) * PenaltySpacing) + (row % 2 == 1 ? PenaltySpacing / 2 : 0.0);

            targets[queue[index]] = FilmSpace.Clamp(Frame(attacking, first + (row * PenaltyRowGap), across), LineMargin);
        }

        KeepKeeper(roster, defending, targets, 0.5);
    }

    /// <summary>
    /// Sets a goal kick or a keeper's ball (`replay-v6`): the side with the ball spreads out, its back line at the edge
    /// of its box and wide, and the other side steps up to the halfway line in three compact lines.
    /// </summary>
    private void ArrangeGoalKick(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        PlaceTaker(roster, state, targets);

        StandInLines(roster, state.Side, 1, GoalKickBack, GoalKickFront, GoalKickBackHalfWidth, GoalKickFrontHalfWidth, targets);
        StandInLines(roster, MatchInputV1.OpponentOf(state.Side), 0, StepUpBack, StepUpFront, StepUpBackHalfWidth, StepUpFrontHalfWidth, targets);
    }

    /// <summary>
    /// Stands a side's outfield players in their lines, the back line at one depth from their own goal and the front line
    /// at another, each line spread across the pitch in the order its anchors give.
    /// </summary>
    private void StandInLines(FilmRoster roster, MatchSide side, int phaseIndex, double backDepth, double frontDepth, double backHalfWidth, double frontHalfWidth, Vec[] targets)
    {
        var count = _lineCount[(int)side, phaseIndex];
        var lines = new List<int>[count];

        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(side, slot);

            if (!roster.IsOccupied(entity) || _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            (lines[_lineOf[entity, phaseIndex]] ??= []).Add(entity);
        }

        for (var line = 0; line < count; line++)
        {
            if (lines[line] is not { Count: > 0 } members)
            {
                continue;
            }

            var t = count <= 1 ? 0.0 : line / (count - 1.0);
            var depth = backDepth + ((frontDepth - backDepth) * t);
            var half = backHalfWidth + ((frontHalfWidth - backHalfWidth) * t);

            // A pair of centre-backs stand close together; a back four reach the full width.
            var span = half * Math.Min(1.0, (members.Count - 1) / 3.0);
            var ordered = members.OrderBy(entity => _anchors[entity, phaseIndex].Y).ThenBy(entity => entity).ToArray();

            for (var index = 0; index < ordered.Length; index++)
            {
                var across = ordered.Length == 1 ? 0.0 : -span + (2.0 * span * index / (ordered.Length - 1));

                // Depth is from the side's own goal, so it is the frame of the side attacking the other way.
                targets[ordered[index]] = FilmSpace.Clamp(Frame(MatchInputV1.OpponentOf(side), depth, across), LineMargin);
            }
        }
    }

    /// <summary>Puts the player taking a set piece on the ball, and gets who he is, or -1.</summary>
    private static int PlaceTaker(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        if (state.Taker < 0 || !roster.IsOccupied(state.Taker) || FilmRoster.SideOf(state.Taker) != state.Side)
        {
            return -1;
        }

        targets[state.Taker] = state.Anchor;

        return state.Taker;
    }

    /// <summary>Moves any outfield player of a side who is within ten yards of the ball out to it, as the rules ask of a free kick.</summary>
    private void KeepOff(FilmRoster roster, MatchSide side, Vec spot, Vec[] targets)
    {
        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(side, slot);

            if (!roster.IsOccupied(entity) || _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                continue;
            }

            var between = targets[entity] - spot;

            if (between.Length >= FreeKickGap - 0.1)
            {
                continue;
            }

            var away = between.Length < 1e-6 ? (FilmSpace.OwnGoal(side) - spot).Unit() : between.Unit();
            var moved = FilmSpace.Clamp(spot + (away * FreeKickGap), LineMargin);

            // Against a touchline there is no room that way: go the other, in towards the pitch.
            if (moved.DistanceTo(spot) < FreeKickGap - 0.1)
            {
                moved = FilmSpace.Clamp(spot + ((FilmSpace.Centre - spot).Unit() * FreeKickGap), LineMargin);
            }

            targets[entity] = moved;
        }
    }

    /// <summary>Moves a point a little away from the ball if it is closer to it than a given distance.</summary>
    private static Vec AwayFrom(Vec point, Vec spot, double distance)
    {
        var between = point - spot;

        if (between.Length >= distance)
        {
            return point;
        }

        var away = between.Length < 1e-6 ? new Vec(0, 1) : between.Unit();

        return FilmSpace.Clamp(spot + (away * distance), LineMargin);
    }

    /// <summary>Moves a point that is in the penalty area out to just beyond its edge.</summary>
    private static Vec OutsideBox(MatchSide attacking, Vec point)
    {
        var along = attacking == MatchSide.Home ? FilmSpace.Length - point.X : point.X;

        if (along >= PenaltyBoxDepth + 1.0 || Math.Abs(point.Y - (FilmSpace.Width / 2)) > PenaltyBoxHalfWidth + 1.0)
        {
            return point;
        }

        return Frame(attacking, PenaltyBoxDepth + 5.0, point.Y - (FilmSpace.Width / 2));
    }

    private void ArrangeCelebration(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var opponents = MatchInputV1.OpponentOf(state.Side);
        var celebration = state.Anchor;

        if (state.Scorer >= 0 && roster.IsOccupied(state.Scorer))
        {
            targets[state.Scorer] = celebration;

            // Three team-mates run to join the scorer; the rest drift back towards the kick-off.
            var joining = Order(roster, state.Side, MatchPositionFamily.Attack)
                .Where(entity => entity != state.Scorer)
                .OrderBy(entity => targets[entity].DistanceTo(celebration))
                .Take(3)
                .ToArray();

            for (var index = 0; index < joining.Length; index++)
            {
                var angle = (index - 1) * 1.1;
                var around = new Vec(Math.Cos(angle) * 2.2, Math.Sin(angle) * 2.2 + 2.2);

                targets[joining[index]] = FilmSpace.Clamp(celebration + around, LineMargin);
            }
        }

        // The side that conceded walks back to its own half for the restart.
        var restart = new ShapeState(FormationMode.KickOff, opponents, FilmSpace.Centre);
        var kickOff = new Vec[FilmRoster.Size];

        FillOpen(roster, opponents, FilmSpace.Centre, kickOff, separate: false);
        ArrangeKickOff(roster, restart, kickOff);

        for (var entity = 0; entity < FilmRoster.Size; entity++)
        {
            if (roster.IsOccupied(entity) && FilmRoster.SideOf(entity) == opponents)
            {
                targets[entity] = kickOff[entity];
            }
        }
    }

    private void KeepKeeper(FilmRoster roster, MatchSide side, Vec[] targets, double offLine, double acrossShift = 0.0)
    {
        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(side, slot);

            if (roster.IsOccupied(entity) && _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                var goal = FilmSpace.OwnGoal(side);

                targets[entity] = new Vec(goal.X + (FilmSpace.Direction(side) * offLine), goal.Y + acrossShift);
            }
        }
    }

    /// <summary>Lists a side's occupied outfield entities, the preferred family first and then by slot.</summary>
    private int[] Order(FilmRoster roster, MatchSide side, MatchPositionFamily first)
    {
        var entities = new List<int>(10);

        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(side, slot);

            if (roster.IsOccupied(entity) && _context.Slots[entity].Family != MatchPositionFamily.Goalkeeper)
            {
                entities.Add(entity);
            }
        }

        return
        [
            .. entities
                .OrderBy(entity => Rank(_context.Slots[entity].Family, first))
                .ThenBy(entity => entity),
        ];
    }

    private static int Rank(MatchPositionFamily family, MatchPositionFamily first)
    {
        if (family == first)
        {
            return 0;
        }

        // Attacking lists run forwards, then midfield, then defence; defending lists the other way round.
        return first == MatchPositionFamily.Attack
            ? 3 - (int)family
            : (int)family;
    }
}

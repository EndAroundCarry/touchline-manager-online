using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>What the shape is arranged around (`replay-v4`).</summary>
/// <param name="Mode">How the players stand.</param>
/// <param name="Side">The side with the ball, or taking the set piece, or celebrating.</param>
/// <param name="Anchor">The corner flag, the free-kick or penalty spot, or the goal a celebration begins at.</param>
/// <param name="Scorer">The entity celebrating, or -1.</param>
internal readonly record struct ShapeState(FormationMode Mode, MatchSide Side, Vec Anchor, int Scorer = -1);

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
/// Where the twenty-two players want to be (`replay-v4`, lines and phases `replay-v6`).
/// </summary>
/// <remarks>
/// <para>
/// The shape starts from the tactics board: each slot's anchor is <see cref="TacticalFormationResolver.Orient"/>
/// as the resolver gives it, moved by the side's real instructions — mentality, defensive line, width, and the
/// press — through the same resolver the simulation uses, with the ball held at the centre so the ball's own pull
/// can be applied here instead.
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
                ArrangeFreeKick(roster, state, targets);
                break;

            case FormationMode.Penalty:
                FillOpen(roster, state.Side, FilmSpace.Centre, targets, separate: false);
                ArrangePenalty(roster, state, targets);
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

    private void ArrangeCorner(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var attackers = Order(roster, state.Side, MatchPositionFamily.Attack);
        var defenders = Order(roster, MatchInputV1.OpponentOf(state.Side), MatchPositionFamily.Defence);

        // Attacking-frame coordinates: metres from the goal line, metres off the centre line.
        (double Along, double Across)[] attackerPoints =
        [
            (6, 4), (8, -3), (10, 0), (12, 6), (11, -7), (15, 2), (19, 9), (19, -9),
        ];

        (double Along, double Across)[] defenderPoints =
        [
            (3, -4), (4, 3), (5, 0), (7, -6), (8, 6), (10, -2), (12, 3), (17, 0),
        ];

        // The taker is the one standing on the flag; the beats pin them there, so they are left out of the box.
        for (var index = 0; index < attackers.Length && index < attackerPoints.Length; index++)
        {
            var point = attackerPoints[index];

            targets[attackers[index]] = Frame(state.Side, point.Along, point.Across);
        }

        for (var index = 0; index < defenders.Length && index < defenderPoints.Length; index++)
        {
            var point = defenderPoints[index];

            // The frame is measured from the goal the corner is taken at, which is the defenders' own.
            targets[defenders[index]] = Frame(state.Side, point.Along, point.Across);
        }

        KeepKeeper(roster, MatchInputV1.OpponentOf(state.Side), targets, 1.2);
    }

    private void ArrangeFreeKick(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var defending = MatchInputV1.OpponentOf(state.Side);
        var goal = FilmSpace.AttackedGoal(state.Side);
        var toGoal = (goal - state.Anchor).Unit();
        var across = new Vec(-toGoal.Y, toGoal.X);

        // The wall: the four defenders nearest the ball stand ten yards from it, on the line to goal.
        var ranked = Order(roster, defending, MatchPositionFamily.Defence)
            .OrderBy(entity => targets[entity].DistanceTo(state.Anchor))
            .ToArray();

        var wall = Math.Min(4, ranked.Length);

        for (var index = 0; index < wall; index++)
        {
            var offset = (index - ((wall - 1) / 2.0)) * 0.9;

            targets[ranked[index]] = FilmSpace.Clamp(state.Anchor + (toGoal * 9.15) + (across * offset), LineMargin);
        }

        // Two attackers wait at the edge of the box for a rebound.
        var waiting = Order(roster, state.Side, MatchPositionFamily.Attack);

        for (var index = 0; index < waiting.Length && index < 3; index++)
        {
            targets[waiting[index]] = Frame(state.Side, 19, (index - 1) * 8);
        }

        KeepKeeper(roster, defending, targets, 1.5);
    }

    private void ArrangePenalty(FilmRoster roster, ShapeState state, Vec[] targets)
    {
        var defending = MatchInputV1.OpponentOf(state.Side);
        var players = Order(roster, state.Side, MatchPositionFamily.Attack)
            .Concat(Order(roster, defending, MatchPositionFamily.Defence))
            .ToArray();

        // Everyone but the taker and the keeper waits outside the box and the arc, spread along it.
        for (var index = 0; index < players.Length; index++)
        {
            var across = ((index % 9) - 4) * 3.2;
            var along = 19.5 + (index >= 9 ? 2.0 : 0.0);

            targets[players[index]] = Frame(state.Side, along, across);
        }

        KeepKeeper(roster, defending, targets, 0.5);
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

    private void KeepKeeper(FilmRoster roster, MatchSide side, Vec[] targets, double offLine)
    {
        for (var slot = 1; slot <= 11; slot++)
        {
            var entity = FilmRoster.Index(side, slot);

            if (roster.IsOccupied(entity) && _context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
            {
                var goal = FilmSpace.OwnGoal(side);

                targets[entity] = new Vec(goal.X + (FilmSpace.Direction(side) * offLine), goal.Y);
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

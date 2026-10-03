using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>What the shape is arranged around (`replay-v4`).</summary>
/// <param name="Mode">How the players stand.</param>
/// <param name="Side">The side with the ball, or taking the set piece, or celebrating.</param>
/// <param name="Anchor">The corner flag, the free-kick or penalty spot, or the goal a celebration begins at.</param>
/// <param name="Scorer">The entity celebrating, or -1.</param>
internal readonly record struct ShapeState(FormationMode Mode, MatchSide Side, Vec Anchor, int Scorer = -1);

/// <summary>
/// Where the twenty-two players want to be (`replay-v4`).
/// </summary>
/// <remarks>
/// <para>
/// The shape starts from the tactics board: each slot's anchor is <see cref="TacticalFormationResolver.Orient"/>
/// as the resolver gives it, moved by the side's real instructions — mentality, defensive line, width, and the
/// press — through the same resolver the simulation uses, with the ball held at the centre so the ball's own pull
/// can be applied here instead. The block then follows the ball, about forty per cent of the way along the pitch
/// and thirty across; it is compact out of possession and stretched in it; and the goalkeeper stands on the line
/// between the ball and the goal.
/// </para>
/// <para>
/// The resolver itself is left alone, because the spatial play model's tests pin it. Set pieces and the
/// celebration override the shape for the players they involve.
/// </para>
/// </remarks>
internal sealed class FilmShape
{
    /// <summary>How far along the pitch the block follows the ball from the centre spot.</summary>
    private const double AlongPull = 0.40;

    /// <summary>How far across the pitch the block follows the ball from the centre line.</summary>
    private const double AcrossPull = 0.30;

    /// <summary>How tightly the block closes up out of possession, along and across the pitch.</summary>
    private static readonly (double Along, double Across) Compact = (0.90, 0.85);

    /// <summary>How the block opens up in possession, along and across the pitch.</summary>
    private static readonly (double Along, double Across) Stretched = (1.0, 1.04);

    /// <summary>How far inside the lines an outfield player is kept.</summary>
    private const double LineMargin = 1.5;

    /// <summary>The centre circle's radius, in metres.</summary>
    private const double CircleRadius = 9.15;

    private readonly FilmContext _context;

    // Indexed [entity][0 = out of possession, 1 = in possession]; the anchors do not depend on the ball.
    private readonly Vec[,] _anchors = new Vec[FilmRoster.Size, 2];
    private readonly Vec[,] _centroids = new Vec[2, 2];

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
                double x = 0, y = 0;
                var count = 0;

                for (var slot = 1; slot <= 11; slot++)
                {
                    var entity = FilmRoster.Index(side, slot);

                    if (context.Slots[entity].Family == MatchPositionFamily.Goalkeeper)
                    {
                        continue;
                    }

                    x += _anchors[entity, phase].X;
                    y += _anchors[entity, phase].Y;
                    count++;
                }

                _centroids[(int)side, phase] = count == 0 ? FilmSpace.Centre : new Vec(x / count, y / count);
            }
        }
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
                FillOpen(roster, state.Side, FilmSpace.Centre, targets);
                ArrangeKickOff(roster, state, targets);
                break;

            case FormationMode.Corner:
                FillOpen(roster, state.Side, FilmSpace.Centre, targets);
                ArrangeCorner(roster, state, targets);
                break;

            case FormationMode.FreeKickShot:
                FillOpen(roster, state.Side, state.Anchor, targets);
                ArrangeFreeKick(roster, state, targets);
                break;

            case FormationMode.Penalty:
                FillOpen(roster, state.Side, FilmSpace.Centre, targets);
                ArrangePenalty(roster, state, targets);
                break;

            case FormationMode.Celebration:
                FillOpen(roster, state.Side, state.Anchor, targets);
                ArrangeCelebration(roster, state, targets);
                break;

            default:
                FillOpen(roster, state.Side, focus, targets);
                break;
        }
    }

    /// <summary>Fills the open-play shape: the block follows the focus, compact or stretched, with the keepers on their lines.</summary>
    /// <param name="roster">Who is on the pitch.</param>
    /// <param name="possession">The side with the ball.</param>
    /// <param name="focus">The point the block follows.</param>
    /// <param name="targets">The targets, written in place.</param>
    public void FillOpen(FilmRoster roster, MatchSide possession, Vec focus, Vec[] targets)
    {
        var shift = new Vec(AlongPull * (focus.X - (FilmSpace.Length / 2)), AcrossPull * (focus.Y - (FilmSpace.Width / 2)));

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var phase = side == possession ? 1 : 0;
            var factor = phase == 1 ? Stretched : Compact;
            var centroid = _centroids[(int)side, phase];

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

                var anchor = _anchors[entity, phase];
                var offset = anchor - centroid;

                targets[entity] = FilmSpace.Clamp(
                    new Vec(
                        centroid.X + (offset.X * factor.Along) + shift.X,
                        centroid.Y + (offset.Y * factor.Across) + shift.Y),
                    LineMargin);
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

        FillOpen(roster, opponents, FilmSpace.Centre, kickOff);
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

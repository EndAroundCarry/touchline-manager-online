using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>A point or a vector on the pitch, in metres (`replay-v4`).</summary>
/// <remarks>
/// The film works in metres so that every speed, acceleration and distance it reasons about means what it says.
/// The engine's pitch units and the presentation's normalized units are converted at the two edges only.
/// </remarks>
/// <param name="X">Metres along the pitch, 0…105; the home side attacks towards the high end.</param>
/// <param name="Y">Metres across the pitch, 0…68.</param>
internal readonly record struct Vec(double X, double Y)
{
    /// <summary>Gets the zero vector.</summary>
    public static Vec Zero => new(0, 0);

    /// <summary>Gets the length of the vector.</summary>
    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public static Vec operator +(Vec left, Vec right) => new(left.X + right.X, left.Y + right.Y);

    public static Vec operator -(Vec left, Vec right) => new(left.X - right.X, left.Y - right.Y);

    public static Vec operator *(Vec vector, double scale) => new(vector.X * scale, vector.Y * scale);

    /// <summary>Gets the distance to another point.</summary>
    /// <param name="other">The other point.</param>
    public double DistanceTo(Vec other) => (this - other).Length;

    /// <summary>Gets the point a fraction of the way to another.</summary>
    /// <param name="other">The other point.</param>
    /// <param name="fraction">0 is this point, 1 is the other.</param>
    public Vec Lerp(Vec other, double fraction) =>
        new(X + ((other.X - X) * fraction), Y + ((other.Y - Y) * fraction));

    /// <summary>Gets the unit vector in the same direction, or zero for a zero vector.</summary>
    public Vec Unit()
    {
        var length = Length;

        return length < 1e-9 ? Zero : new Vec(X / length, Y / length);
    }
}

/// <summary>The pitch, in metres, and the conversions at its edges (`replay-v4`).</summary>
internal static class FilmSpace
{
    /// <summary>The pitch's length, in metres.</summary>
    public const double Length = 105.0;

    /// <summary>The pitch's width, in metres.</summary>
    public const double Width = 68.0;

    /// <summary>The normalized scale the presentation speaks, on both axes.</summary>
    public const int Normalized = 10_000;

    /// <summary>How many metres one unit of ball altitude is: the crossbar, 2.44 m, is thirty units.</summary>
    public const double MetresPerAltitude = 2.44 / 30.0;

    /// <summary>Converts an engine pitch point (10,000 × 7,000) to metres.</summary>
    /// <param name="x">Across the pitch, 0…10,000.</param>
    /// <param name="y">Down the pitch, 0…7,000.</param>
    public static Vec FromEngine(int x, int y) =>
        new(x * Length / SpatialPitchLength, y * Width / SpatialPitchWidth);

    /// <summary>Converts a point in metres to the presentation's normalized X.</summary>
    /// <param name="metres">Metres along the pitch.</param>
    public static int NormalizeX(double metres) =>
        int.Clamp((int)Math.Round(metres / Length * Normalized), 0, Normalized);

    /// <summary>Converts a point in metres to the presentation's normalized Y.</summary>
    /// <param name="metres">Metres across the pitch.</param>
    public static int NormalizeY(double metres) =>
        int.Clamp((int)Math.Round(metres / Width * Normalized), 0, Normalized);

    /// <summary>Converts a normalized X back to metres.</summary>
    /// <param name="normalized">The normalized X.</param>
    public static double MetresX(int normalized) => normalized * Length / Normalized;

    /// <summary>Converts a normalized Y back to metres.</summary>
    /// <param name="normalized">The normalized Y.</param>
    public static double MetresY(int normalized) => normalized * Width / Normalized;

    /// <summary>Clamps a point to the pitch.</summary>
    /// <param name="point">The point.</param>
    /// <param name="margin">How far inside the lines to stay.</param>
    public static Vec Clamp(Vec point, double margin = 0.0) =>
        new(
            double.Clamp(point.X, margin, Length - margin),
            double.Clamp(point.Y, margin, Width - margin));

    /// <summary>Gets the centre spot.</summary>
    public static Vec Centre => new(Length / 2, Width / 2);

    /// <summary>Gets how far up the pitch a point is on a side's own scale: 0 at its own goal line.</summary>
    /// <param name="point">The point.</param>
    /// <param name="side">The side.</param>
    public static double Attacking(Vec point, MatchSide side) =>
        side == MatchSide.Home ? point.X : Length - point.X;

    /// <summary>Gets the centre of the goal a side attacks.</summary>
    /// <param name="side">The side attacking.</param>
    public static Vec AttackedGoal(MatchSide side) => new(side == MatchSide.Home ? Length : 0, Width / 2);

    /// <summary>Gets the centre of the goal a side defends.</summary>
    /// <param name="side">The side defending.</param>
    public static Vec OwnGoal(MatchSide side) => new(side == MatchSide.Home ? 0 : Length, Width / 2);

    /// <summary>Gets the unit step along the pitch in the direction a side attacks.</summary>
    /// <param name="side">The side.</param>
    public static double Direction(MatchSide side) => side == MatchSide.Home ? 1.0 : -1.0;

    private const double SpatialPitchLength = 10_000.0;
    private const double SpatialPitchWidth = 7_000.0;
}

/// <summary>What a beat of the film is (`replay-v4`).</summary>
internal enum BeatKind
{
    /// <summary>A player drives the ball.</summary>
    Carry = 0,

    /// <summary>A ground pass.</summary>
    Pass = 1,

    /// <summary>A pass in the air.</summary>
    LoftedPass = 2,

    /// <summary>A cross from a wide area into the box.</summary>
    Cross = 3,

    /// <summary>Two players meet a ball in the air.</summary>
    Header = 4,

    /// <summary>A strike at goal.</summary>
    Shot = 5,

    /// <summary>The ball is cleared.</summary>
    Clearance = 6,

    /// <summary>A challenge on the ball.</summary>
    Duel = 7,

    /// <summary>The goalkeeper stops the ball.</summary>
    Save = 8,

    /// <summary>The ball is put back where a dead ball is taken from.</summary>
    Placement = 9,

    /// <summary>Play is stopped for a fixed length of film.</summary>
    Hold = 10,
}

/// <summary>Why play is held (`replay-v4`).</summary>
internal enum HoldKind
{
    /// <summary>Not a hold.</summary>
    None = 0,

    /// <summary>A kick-off.</summary>
    KickOff = 1,

    /// <summary>A goal kick.</summary>
    GoalKick = 2,

    /// <summary>The goalkeeper has the ball.</summary>
    KeeperBall = 3,

    /// <summary>A free kick, quick or struck.</summary>
    FreeKick = 4,

    /// <summary>A corner.</summary>
    Corner = 5,

    /// <summary>A penalty.</summary>
    Penalty = 6,

    /// <summary>A goal, and the celebration.</summary>
    Goal = 7,

    /// <summary>A card is shown.</summary>
    Card = 8,

    /// <summary>A substitution or a player leaving the pitch.</summary>
    Substitution = 9,

    /// <summary>The half-time card.</summary>
    HalfTime = 10,
}

/// <summary>Where a strike finished (`replay-v4`).</summary>
internal enum StrikeResult
{
    /// <summary>The beat is not a strike.</summary>
    None = 0,

    /// <summary>In the net.</summary>
    Goal = 1,

    /// <summary>Stopped by the goalkeeper.</summary>
    Saved = 2,

    /// <summary>Wide or over.</summary>
    OffTarget = 3,

    /// <summary>The post or the bar.</summary>
    Woodwork = 4,

    /// <summary>Stopped by a defender.</summary>
    Blocked = 5,
}

/// <summary>How the players stand while a beat is played (`replay-v4`).</summary>
internal enum FormationMode
{
    /// <summary>Open play: the shape follows the ball.</summary>
    Open = 0,

    /// <summary>Each side in its own half, around the centre spot.</summary>
    KickOff = 1,

    /// <summary>Attackers and defenders crowd the box.</summary>
    Corner = 2,

    /// <summary>A wall and a line of attackers.</summary>
    FreeKickShot = 3,

    /// <summary>Everyone waits outside the box.</summary>
    Penalty = 4,

    /// <summary>The scoring side celebrates.</summary>
    Celebration = 5,

    /// <summary>The goalkeeper's side spreads out for a goal kick or a keeper's ball.</summary>
    GoalKick = 6,
}

/// <summary>Where the player who plays a beat comes from (`replay-v4`).</summary>
internal enum ActorSource
{
    /// <summary>The engine named the player.</summary>
    Named = 0,

    /// <summary>The player who received the beat before: the ball is passed on from where it arrived.</summary>
    PreviousReceiver = 1,

    /// <summary>The player of the beat's side nearest the ball when the beat begins.</summary>
    NearestToBall = 2,
}

/// <summary>A moment in a beat at which an event happened (`replay-v4`).</summary>
/// <param name="Sequence">The event's sequence number.</param>
/// <param name="AtStart">Whether the event happened as the beat began rather than as it ended.</param>
internal readonly record struct BeatEvent(int Sequence, bool AtStart);

/// <summary>A player who has to be at a point when a beat ends (`replay-v4`).</summary>
/// <param name="Participant">The player.</param>
/// <param name="Point">Where they must be.</param>
/// <param name="Tolerance">How close counts as being there, in metres.</param>
/// <param name="Soft">Whether the player is driving the ball there, so that being short is not a failure.</param>
internal readonly record struct Pin(Guid Participant, Vec Point, double Tolerance, bool Soft = false);

/// <summary>
/// One beat of the film: a single move of the ball, a challenge, or a hold (`replay-v4`).
/// </summary>
/// <remarks>
/// A possession is turned into a run of beats by <see cref="FilmScript"/>, and the beats are what the timing,
/// the motion and the commentary are built from. A beat's ball path and the players the engine named at it are
/// facts; its duration, its speed and the way the other players move are decided later.
/// </remarks>
internal sealed class FilmBeat
{
    /// <summary>Gets what the beat is.</summary>
    public required BeatKind Kind { get; init; }

    /// <summary>Gets why play is held, for a hold.</summary>
    public HoldKind Hold { get; init; }

    /// <summary>Gets the index of the possession the beat belongs to, or -1 for the half-time card.</summary>
    public required int Possession { get; init; }

    /// <summary>Gets the half the beat is played in.</summary>
    public required int Period { get; init; }

    /// <summary>Gets the side acting on the ball.</summary>
    public required MatchSide Side { get; init; }

    /// <summary>Gets or sets where the ball is when the beat begins.</summary>
    public required Vec From { get; set; }

    /// <summary>Gets or sets where the ball is when the beat ends.</summary>
    public required Vec To { get; set; }

    /// <summary>Gets the ball's altitude when the beat begins, on the engine's 0…100 scale.</summary>
    public double ZFrom { get; set; }

    /// <summary>Gets the ball's altitude when the beat ends.</summary>
    public double ZTo { get; set; }

    /// <summary>Gets how far above the chord from the start altitude to the end altitude the ball arcs.</summary>
    public double ZArc { get; set; }

    /// <summary>Gets the player who plays the ball, or has it at the start of the beat.</summary>
    /// <remarks>Null until the motion has chosen them, when <see cref="ActorSource"/> says they are not named.</remarks>
    public Guid? Actor { get; set; }

    /// <summary>Gets where the actor comes from when the engine did not name them.</summary>
    public ActorSource ActorSource { get; set; }

    /// <summary>Gets the player who receives the ball, or ends the beat with it.</summary>
    /// <remarks>Null until the motion has chosen them, when <see cref="ReceiverPending"/> is set.</remarks>
    public Guid? Receiver { get; set; }

    /// <summary>Gets whether the receiver is the team-mate who can reach the ball soonest, chosen as the beat is played.</summary>
    public bool ReceiverPending { get; set; }

    /// <summary>Gets whether the player who ends the beat is the one who played it: he drives the ball himself.</summary>
    public bool ReceiverIsActor { get; set; }

    /// <summary>Gets the opposing player the beat is against: the tackler, the marker, the fouler, the keeper.</summary>
    public Guid? Opponent { get; set; }

    /// <summary>Gets where a strike finished, for a shot.</summary>
    public StrikeResult Strike { get; set; }

    /// <summary>Gets whether the strike was a header, which is slower than a boot.</summary>
    public bool Headed { get; set; }

    /// <summary>Gets how the players stand during the beat.</summary>
    public FormationMode Formation { get; set; }

    /// <summary>Gets or sets whether the film cuts to a new arrangement as the beat begins.</summary>
    public bool Cut { get; set; }

    /// <summary>Gets the kind of cut, when there is one: <c>kick_off</c> or <c>half_time</c>.</summary>
    public string? CutKind { get; set; }

    /// <summary>Gets or sets whether the beat belongs to a possession the engine played as a counter-attack (`replay-v6`).</summary>
    public bool Counter { get; set; }

    /// <summary>Gets or sets whether the beat is the foul that stops a possession.</summary>
    public bool Foul { get; init; }

    /// <summary>Gets the events that happened at the beat.</summary>
    public List<BeatEvent> Events { get; } = [];

    /// <summary>Gets or sets the beat's natural duration, in seconds of real time.</summary>
    public double NaturalSeconds { get; set; }

    /// <summary>Gets or sets the film length of a hold, in seconds.</summary>
    public double HoldFilmSeconds { get; set; }

    /// <summary>Gets whether the beat is a hold.</summary>
    public bool IsHold => Kind == BeatKind.Hold;

    /// <summary>Gets whether the beat is a ground move that may be merged into its neighbours.</summary>
    public bool IsGroundMove => Kind is BeatKind.Pass or BeatKind.Carry;

    /// <summary>Gets the length of the ball's path, in metres.</summary>
    public double Distance => From.DistanceTo(To);
}

/// <summary>One possession, as the film places it (`replay-v4`).</summary>
/// <param name="Index">The possession's index in the match.</param>
/// <param name="Source">The recorded possession.</param>
/// <param name="FirstBeat">The index of its first beat.</param>
/// <param name="LastBeat">The index of its last beat.</param>
internal sealed record FilmPossession(int Index, MatchPassageV1 Source, int FirstBeat, int LastBeat);

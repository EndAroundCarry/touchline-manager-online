using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.MatchEngine.Tick;

/// <summary>
/// What the role of a slot changes in the tick engine (`wide-midfielders`, Milestone 3): every per-role number sits here, so the
/// tuning is in one place. A role the tick engine has no special play for gets the plain figures of its family.
/// </summary>
/// <remarks>
/// <para>
/// The wide midfielder is the classic wide man of a 4-4-2: he runs the wing and crosses, often from deep, rarely shoots, and tracks back
/// after the opposing full-back or winger. The winger is the other way about: he stays high, goes to the byline, shoots from the wing
/// when he gets the chance, and does no screening. Every function here is pure and draws no random numbers.
/// </para>
/// </remarks>
internal static class TickRoleProfile
{
    /// <summary>
    /// The goal chance, in basis points, below which a wide midfielder will not shoot: shooting is the striker's work and the winger's
    /// from the wing, and he keeps the ball for the cross (against <see cref="TickBallCarrierBrain.ShotMinimumChance"/>).
    /// </summary>
    public const int WideMidfielderShotMinimumChance = 2_700;

    /// <summary>The goal chance, in basis points, below which a wide midfielder will not shoot from beyond the long-shot range: above any chance a shot can have, so he never does.</summary>
    public const int WideMidfielderLongShotMinimumChance = TickBallCarrierBrain.MaximumGoalChance + 1;

    /// <summary>The nearest to the goal line a wide midfielder crosses from, in pitch units (68 m from his own goal line): he crosses early, from deep.</summary>
    public const int WideMidfielderCrossMinimumX = 6_800;

    /// <summary>What a cross is worth more to a wide midfielder, in utility: he prefers to deliver the ball to cutting in.</summary>
    public const int WideMidfielderCrossBonus = 300;

    /// <summary>The farthest from a wide midfielder an opponent is for him to track, in pitch units (25 m).</summary>
    public const int TrackZone = 2_500;

    /// <summary>How far goal-side of the man he tracks a wide midfielder stands, in pitch units (15 m): a loose shadow, not a marker's tight mark (2.7 to 5.7 m). Closer, the tackles that follow made the matches 0.5 goals longer.</summary>
    public const int TrackStandOff = 1_500;

    /// <summary>The pace of a wide midfielder tracking a man, in basis points of top speed: a jog, not a marker's 80%.</summary>
    public const int TrackPaceBasisPoints = 5_000;

    /// <summary>Gets the goal chance, in basis points, a carrier of a role needs before he shoots.</summary>
    /// <param name="role">The carrier's role.</param>
    /// <param name="far">Whether he is beyond the long-shot range.</param>
    public static int ShotMinimumChance(MatchRole role, bool far) => role == MatchRole.WideMidfielder
        ? (far ? WideMidfielderLongShotMinimumChance : WideMidfielderShotMinimumChance)
        : (far ? TickBallCarrierBrain.LongShotMinimumChance : TickBallCarrierBrain.ShotMinimumChance);

    /// <summary>Gets the nearest to the goal line, in pitch units, a carrier of a role crosses from.</summary>
    /// <param name="role">The carrier's role.</param>
    public static int CrossMinimumX(MatchRole role) =>
        role == MatchRole.WideMidfielder ? WideMidfielderCrossMinimumX : TickBallCarrierBrain.CrossMinimumX;

    /// <summary>Gets what a cross is worth more, in utility, to a carrier of a role.</summary>
    /// <param name="role">The carrier's role.</param>
    public static int CrossBonus(MatchRole role) => role == MatchRole.WideMidfielder ? WideMidfielderCrossBonus : 0;

    /// <summary>
    /// Tells whether a man of a role makes the wide runs a side's mentality does not ask of its midfielders. A wide midfielder does at
    /// every mentality but Defensive, and never while his side keeps men back for a counter.
    /// </summary>
    /// <param name="role">The man's role.</param>
    /// <param name="mentality">The side's mentality.</param>
    /// <param name="holding">Whether the side keeps men back for a counter.</param>
    public static bool RunsWide(MatchRole role, MatchMentality mentality, bool holding) =>
        role == MatchRole.WideMidfielder && mentality != MatchMentality.Defensive && !holding;

    /// <summary>Tells whether a man of a role may be sent to screen a passing lane: the winger stays high.</summary>
    /// <param name="role">The man's role.</param>
    public static bool Screens(MatchRole role) => role != MatchRole.Winger;

    /// <summary>Tells whether a man of a role tracks the opposing man on his flank when his side defends.</summary>
    /// <param name="role">The man's role.</param>
    public static bool Tracks(MatchRole role) => role == MatchRole.WideMidfielder;
}

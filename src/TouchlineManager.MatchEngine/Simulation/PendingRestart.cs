using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// A dead ball that the next possession must be played from (`engine-v5`, `MAT-12`).
/// </summary>
/// <remarks>
/// <para>
/// A restart belongs to somebody. The rules say which side takes it and from where, so the next possession is
/// handed its side and its spot instead of drawing them: the conceding side kicks off after a goal, the
/// defending side restarts from its goal area after a save or a miss, the fouled side takes the free kick,
/// and the defending side takes the free kick for an offside.
/// </para>
/// <para>
/// A restart is consumed by the very next possession and never survives it. The old model left a goal-area
/// flag pending until that side next had the ball, so the ball could teleport to a goal area several
/// possessions later; this value is cleared the moment it is read.
/// </para>
/// </remarks>
/// <param name="Side">The side that takes the restart, and so has the next possession.</param>
/// <param name="Kind">What kind of restart it is.</param>
/// <param name="Spot">Where the ball is placed, in pitch coordinates.</param>
internal readonly record struct PendingRestart(MatchSide Side, PassageRestartKind Kind, SpatialPoint Spot);

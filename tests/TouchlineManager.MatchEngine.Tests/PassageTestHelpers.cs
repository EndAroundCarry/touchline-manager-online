using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// What the passage, restart, and clock tests share: a recorded match, and the pitch arithmetic they read it with.
/// </summary>
internal static class PassageTestHelpers
{
    /// <summary>The events that decide what the next possession is: how a shot, or an offside, ended.</summary>
    public static readonly EngineEventType[] OutcomeEvents =
    [
        EngineEventType.Goal,
        EngineEventType.PenaltyGoal,
        EngineEventType.PenaltyMissed,
        EngineEventType.ShotSaved,
        EngineEventType.ShotBlocked,
        EngineEventType.ShotOffTarget,
        EngineEventType.Woodwork,
        EngineEventType.Offside,
    ];

    /// <summary>Simulates one match with the passage recorder attached.</summary>
    /// <param name="seed">The match seed.</param>
    public static RecordedMatch Play(ulong seed)
    {
        var input = TestMatchFactory.Even(seed);
        var recorder = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, EngineRulesV2.Default, passages: recorder);

        return new RecordedMatch(input, result, recorder.Passages);
    }

    /// <summary>Gets how far up the pitch a point is on a side's own scale.</summary>
    public static int AttackingX(int x, MatchSide side) =>
        side == MatchSide.Home ? x : SpatialPitch.PitchLength - x;

    /// <summary>Gets where across the pitch a point is on a side's own scale.</summary>
    public static int AttackingY(int y, MatchSide side) =>
        side == MatchSide.Home ? y : SpatialPitch.PitchWidth - y;

    /// <summary>Gets the side that is not the given one.</summary>
    public static MatchSide Opponent(MatchSide side) =>
        side == MatchSide.Home ? MatchSide.Away : MatchSide.Home;

    /// <summary>Gets the last waypoint recorded at or before an event: where the ball was when it happened.</summary>
    public static PassageWaypointV1 BallAt(MatchPassageV1 passage, PassageEventV1 matchEvent) =>
        passage.Waypoints.Last(waypoint => waypoint.FractionBasisPoints <= matchEvent.FractionBasisPoints);

    /// <summary>Gets the engine event a passage's event refers to.</summary>
    public static EngineEventV1 EventOf(RecordedMatch match, PassageEventV1 passageEvent) =>
        match.Result.Events[passageEvent.Sequence - 1];

    /// <summary>Gets a passage's events, paired with the engine events they refer to.</summary>
    public static IEnumerable<(PassageEventV1 Position, EngineEventV1 Event)> EventsOf(
        RecordedMatch match,
        MatchPassageV1 passage) =>
        passage.Events.Select(position => (position, EventOf(match, position)));

    /// <summary>One simulated match, with the passages it recorded.</summary>
    /// <param name="Input">The frozen snapshot.</param>
    /// <param name="Result">The result.</param>
    /// <param name="Passages">The recorded possessions, in the order they were played.</param>
    public sealed record RecordedMatch(
        MatchInputV1 Input,
        MatchResultV1 Result,
        IReadOnlyList<MatchPassageV1> Passages);
}

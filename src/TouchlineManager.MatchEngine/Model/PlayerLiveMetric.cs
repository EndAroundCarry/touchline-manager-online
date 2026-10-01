namespace TouchlineManager.MatchEngine.Model;

/// <summary>
/// A player's live condition and rating at one minute of the match, for the match center's panels.
/// </summary>
/// <remarks>
/// The figure the viewer showed while the match was being played, captured minute by minute so a replay
/// can show the same curve the live panel did rather than interpolating between kickoff and full time.
/// It carries no hidden value: condition is the bar a manager watches and rating is the badge beside it.
/// </remarks>
/// <param name="ParticipantId">The participant the snapshot belongs to.</param>
/// <param name="Minute">The match minute the snapshot was taken at.</param>
/// <param name="ConditionBasisPoints">Condition on the 0–10,000 scale.</param>
/// <param name="RatingBasisPoints">The live match rating on the 0–10,000 scale.</param>
public sealed record PlayerLiveMetricV1(
    Guid ParticipantId,
    int Minute,
    int ConditionBasisPoints,
    int RatingBasisPoints);

/// <summary>
/// Captures the minute-by-minute condition and live rating a replay is built from (`engine-v3`, §9.5).
/// </summary>
/// <remarks>
/// <para>
/// A recorder rather than a field on the result, deliberately. The result is the engine's hashed output and
/// every stored match is verified against it, so a replay-only fact there would either change the hash of
/// every match ever played or sit outside the hash and go unaudited. Handing the simulation a sink leaves
/// the result byte for byte what it was — capturing consumes no draw and changes no state — while a replay
/// read re-simulates the same frozen input and recovers the same curve the live panels showed.
/// </para>
/// <para>
/// The engine appends; the caller owns the instance and reads <see cref="Metrics"/> when the match is over.
/// Each read of a replay gets its own recorder, so two viewers cannot share one match's capture.
/// </para>
/// </remarks>
public sealed class PlayerLiveMetricsRecorder
{
    private readonly List<PlayerLiveMetricV1> _metrics = [];

    /// <summary>Gets every captured snapshot, in the order the minutes were played, then by slot.</summary>
    public IReadOnlyList<PlayerLiveMetricV1> Metrics => _metrics;

    /// <summary>Gets how many snapshots have been captured.</summary>
    internal int Count => _metrics.Count;

    /// <summary>
    /// Drops every snapshot captured after a point, so the current minute can be captured again.
    /// </summary>
    /// <remarks>
    /// A possession is longer than a minute, so a minute is captured several times as the play moves
    /// through it. The last capture of a minute is the state at the end of that minute, which is what the
    /// match center draws; earlier ones within the same minute are superseded rather than kept, so a
    /// minute never carries two bars for one player.
    /// </remarks>
    /// <param name="count">How many snapshots to keep.</param>
    internal void Truncate(int count) => _metrics.RemoveRange(count, _metrics.Count - count);

    /// <summary>Captures one player's condition and live rating at a minute.</summary>
    /// <param name="participantId">The player.</param>
    /// <param name="minute">The match minute.</param>
    /// <param name="conditionBasisPoints">Condition on the 0–10,000 scale.</param>
    /// <param name="ratingBasisPoints">The live match rating on the 0–10,000 scale.</param>
    internal void Record(Guid participantId, int minute, int conditionBasisPoints, int ratingBasisPoints) =>
        _metrics.Add(new PlayerLiveMetricV1(participantId, minute, conditionBasisPoints, ratingBasisPoints));
}

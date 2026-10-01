using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Simulates continuous physical fatigue and stamina drain across the 90+ minutes of a match.
/// High fatigue degrades effective pace, acceleration, and composure, leading to late-game lapses.
/// </summary>
public sealed class FatigueManager
{
    private readonly Dictionary<Guid, int> _conditions = [];

    public void RegisterPlayer(Guid participantId, int kickoffCondition)
    {
        _conditions[participantId] = kickoffCondition;
    }

    public int GetCondition(Guid participantId) =>
        _conditions.TryGetValue(participantId, out var cond) ? cond : 10_000;

    /// <summary>
    /// Applies fatigue drain over a passage of play based on player stamina and team tempo/pressing instructions.
    /// </summary>
    public void ApplyDrain(
        MatchParticipantV1 player,
        MatchInstructionsV1 instructions,
        int durationSeconds,
        bool wasInvolvedInSprint)
    {
        var stamina = player.Attributes.ValueOf(MatchAttributeName.Stamina);
        var workRate = player.Attributes.ValueOf(MatchAttributeName.WorkRate);

        // Lower stamina -> faster drain
        var baseDrainRate = 24 - stamina; // 4..23 per minute

        if (instructions.Tempo == MatchTempo.High)
        {
            baseDrainRate = (int)(baseDrainRate * 1.25);
        }

        if (instructions.Pressing == MatchPressing.HighPress)
        {
            baseDrainRate = (int)(baseDrainRate * 1.20);
        }

        if (wasInvolvedInSprint)
        {
            baseDrainRate += 10;
        }

        var drain = (baseDrainRate * durationSeconds * 10) / 60; // in basis points
        var current = GetCondition(player.ParticipantId);
        _conditions[player.ParticipantId] = Math.Max(3_500, current - drain); // Floor at 35%
    }

    /// <summary>
    /// Applies half-time recovery.
    /// </summary>
    public void ApplyHalfTimeRecovery(Guid participantId)
    {
        var current = GetCondition(participantId);
        _conditions[participantId] = Math.Min(10_000, current + 600); // +6% recovery at half-time
    }
}

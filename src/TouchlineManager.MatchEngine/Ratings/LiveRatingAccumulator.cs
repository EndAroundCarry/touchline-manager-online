namespace TouchlineManager.MatchEngine.Ratings;

/// <summary>
/// Tracks and accumulates dynamic, real-time match ratings for all players on the pitch.
/// Matches Football Manager conventions where players start at 6.0 and fluctuate based on live actions.
/// </summary>
public sealed class LiveRatingAccumulator
{
    private const int BaselineRatingBasisPoints = 6_000; // 6.0 baseline
    private readonly Dictionary<Guid, int> _ratings = [];

    public void RegisterPlayer(Guid participantId)
    {
        _ratings[participantId] = BaselineRatingBasisPoints;
    }

    public int GetRating(Guid participantId) =>
        _ratings.TryGetValue(participantId, out var rating) ? rating : BaselineRatingBasisPoints;

    public void RecordCompletedPass(Guid participantId, bool isKeyPass = false)
    {
        Adjust(participantId, isKeyPass ? 300 : 30); // +0.3 for key pass, +0.03 for pass
    }

    public void RecordInterception(Guid participantId)
    {
        Adjust(participantId, 80); // +0.08
    }

    public void RecordTackle(Guid participantId, bool won)
    {
        Adjust(participantId, won ? 120 : -80); // +0.12 won, -0.08 lost
    }

    public void RecordAerialDuel(Guid participantId, bool won)
    {
        Adjust(participantId, won ? 80 : -50);
    }

    public void RecordShot(Guid participantId, bool onTarget, bool isGoal)
    {
        if (isGoal)
        {
            Adjust(participantId, 800); // +0.80 for goal
        }
        else if (onTarget)
        {
            Adjust(participantId, 100); // +0.10 for on target
        }
        else
        {
            Adjust(participantId, -40); // -0.04 for off target
        }
    }

    public void RecordAssist(Guid participantId)
    {
        Adjust(participantId, 450); // +0.45 for assist
    }

    public void RecordSave(Guid participantId, bool isPenalty = false)
    {
        Adjust(participantId, isPenalty ? 900 : 250); // +0.25 save, +0.90 penalty save
    }

    public void RecordGoalConceded(Guid participantId)
    {
        Adjust(participantId, -250); // -0.25 for GK and defenders
    }

    public void RecordCard(Guid participantId, bool isRed)
    {
        Adjust(participantId, isRed ? -1200 : -200); // -1.2 for red card, -0.2 for yellow
    }

    public void RecordError(Guid participantId)
    {
        Adjust(participantId, -600); // -0.60 for mistake leading to goal
    }

    private void Adjust(Guid participantId, int deltaBasisPoints)
    {
        var current = GetRating(participantId);
        _ratings[participantId] = Math.Clamp(current + deltaBasisPoints, 3_000, 10_000); // 3.0 to 10.0 scale
    }
}

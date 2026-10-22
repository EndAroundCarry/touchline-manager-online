using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Domain.World;

/// <summary>
/// One step of a club's standing at the end of a season, kept for ever (`STD-3`).
/// </summary>
/// <remarks>
/// Append-only. A season moves a club's standing once (<see cref="ClubStanding.Step"/>), and the row says from what, to what and why, so the
/// history of how a club became rich is something that can be read back and not only the number it ended on.
/// </remarks>
public sealed class ClubStandingEvent
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubStandingEvent()
    {
    }

    /// <summary>Gets the event identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the season the step closes.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets the tier the club played the season in.</summary>
    public int FinishedTier { get; private set; }

    /// <summary>Gets the tier the club plays in next.</summary>
    public int NextTier { get; private set; }

    /// <summary>Gets the place the club finished in.</summary>
    public int FinalPlace { get; private set; }

    /// <summary>Gets how the season's objective turned out, or null when the club had none.</summary>
    public ObjectiveOutcome? Outcome { get; private set; }

    /// <summary>Gets the standing before the step, in hundredths of a point.</summary>
    public int BeforeCentis { get; private set; }

    /// <summary>Gets the standing after the step, in hundredths of a point.</summary>
    public int AfterCentis { get; private set; }

    /// <summary>Gets when the step was taken.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Records a step.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="seasonId">The season.</param>
    /// <param name="finishedTier">The tier the club played the season in.</param>
    /// <param name="nextTier">The tier it plays in next.</param>
    /// <param name="finalPlace">The place it finished in.</param>
    /// <param name="outcome">How its objective turned out, or null.</param>
    /// <param name="beforeCentis">The standing before the step.</param>
    /// <param name="afterCentis">The standing after the step.</param>
    /// <param name="now">The current instant.</param>
    public static ClubStandingEvent Record(
        Guid id,
        Guid clubId,
        Guid seasonId,
        int finishedTier,
        int nextTier,
        int finalPlace,
        ObjectiveOutcome? outcome,
        int beforeCentis,
        int afterCentis,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(finishedTier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextTier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(finalPlace, 1);

        return new ClubStandingEvent
        {
            Id = id,
            ClubId = clubId,
            SeasonId = seasonId,
            FinishedTier = finishedTier,
            NextTier = nextTier,
            FinalPlace = finalPlace,
            Outcome = outcome,
            BeforeCentis = beforeCentis,
            AfterCentis = afterCentis,
            OccurredAt = now,
        };
    }
}

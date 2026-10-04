namespace TouchlineManager.Domain.Squad;

/// <summary>
/// A club's optional training programme override for one player (`TRN-1`, `TRN-2`).
/// </summary>
/// <remarks>
/// A row is optional per player: with none, the player trains the programme matching their primary position
/// (<see cref="TrainingProgrammes.DefaultFor"/>), and the daily progression job uses the row's programme
/// where one exists. The class keeps its name from the attribute-family focus it replaced.
/// </remarks>
public sealed class PlayerTrainingFocus
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerTrainingFocus()
    {
    }

    /// <summary>Gets the focus identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the player the programme applies to.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the club the player belonged to when the programme was set.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the training programme the manager chose, which overrides the position default.</summary>
    public TrainingProgramme Programme { get; private set; }

    /// <summary>Gets the date the programme takes effect from.</summary>
    public DateOnly EffectiveDate { get; private set; }

    /// <summary>Gets when the focus was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the focus was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Sets a player's training programme override (`TRN-1`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="playerId">The player the programme applies to.</param>
    /// <param name="clubId">The club the player belongs to.</param>
    /// <param name="programme">The programme to train.</param>
    /// <param name="effectiveDate">The date the programme takes effect from.</param>
    /// <param name="now">The current instant.</param>
    public static PlayerTrainingFocus Set(
        Guid id,
        Guid playerId,
        Guid clubId,
        TrainingProgramme programme,
        DateOnly effectiveDate,
        DateTimeOffset now) => new()
        {
            Id = id,
            PlayerId = playerId,
            ClubId = clubId,
            Programme = programme,
            EffectiveDate = effectiveDate,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>Changes the programme and effective date.</summary>
    /// <param name="programme">The programme to train.</param>
    /// <param name="effectiveDate">The date the programme takes effect from.</param>
    /// <param name="now">The current instant.</param>
    public void Revise(TrainingProgramme programme, DateOnly effectiveDate, DateTimeOffset now)
    {
        Programme = programme;
        EffectiveDate = effectiveDate;

        UpdatedAt = now;
        Version++;
    }
}

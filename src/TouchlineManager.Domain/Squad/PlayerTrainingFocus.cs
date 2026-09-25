namespace TouchlineManager.Domain.Squad;

/// <summary>
/// A club's optional individual training instruction for one player (`TRN-2`).
/// </summary>
/// <remarks>
/// The individual focus selects one attribute family, which is why the family enum is shared with
/// attributes rather than repeated here. A row is optional per player: most players train only with the
/// club's team focus, and the daily progression job uses the individual focus where one exists.
/// </remarks>
public sealed class PlayerTrainingFocus
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerTrainingFocus()
    {
    }

    /// <summary>Gets the focus identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the player the focus applies to.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the club the player belonged to when the focus was set.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the attribute family the player focuses on.</summary>
    public AttributeFamily FocusFamily { get; private set; }

    /// <summary>Gets the date the focus takes effect from.</summary>
    public DateOnly EffectiveDate { get; private set; }

    /// <summary>Gets when the focus was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the focus was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Sets a player's individual training focus.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="playerId">The player the focus applies to.</param>
    /// <param name="clubId">The club the player belongs to.</param>
    /// <param name="focusFamily">The attribute family to focus on.</param>
    /// <param name="effectiveDate">The date the focus takes effect from.</param>
    /// <param name="now">The current instant.</param>
    public static PlayerTrainingFocus Set(
        Guid id,
        Guid playerId,
        Guid clubId,
        AttributeFamily focusFamily,
        DateOnly effectiveDate,
        DateTimeOffset now) => new()
        {
            Id = id,
            PlayerId = playerId,
            ClubId = clubId,
            FocusFamily = focusFamily,
            EffectiveDate = effectiveDate,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>Changes the family and effective date.</summary>
    /// <param name="focusFamily">The attribute family to focus on.</param>
    /// <param name="effectiveDate">The date the focus takes effect from.</param>
    /// <param name="now">The current instant.</param>
    public void Revise(AttributeFamily focusFamily, DateOnly effectiveDate, DateTimeOffset now)
    {
        FocusFamily = focusFamily;
        EffectiveDate = effectiveDate;

        UpdatedAt = now;
        Version++;
    }
}

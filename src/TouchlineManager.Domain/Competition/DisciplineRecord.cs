namespace TouchlineManager.Domain.Competition;

/// <summary>
/// A player's card accumulation for one division-season, and the basis of the suspension rules
/// (`DIS-2`…`DIS-4`, master plan §6.4).
/// </summary>
/// <remarks>
/// <para>
/// One row per player per division-season, advanced by the match effects the publication applies. It
/// carries counts and nothing derived from them: whether a booking crosses the yellow threshold is a
/// question the record answers about a hypothetical set of cards (<see cref="YellowSuspensionsEarned"/>)
/// rather than a state it keeps, because the suspension itself is a <c>PlayerUnavailability</c> row and a
/// second copy of "how many matches are left" would be two answers to one question.
/// </para>
/// <para>
/// The count is reset at season rollover and nowhere else (`DIS-3`): a player who serves a suspension keeps
/// their accumulation, so the fifth booking of the season and the tenth each earn one.
/// </para>
/// </remarks>
public sealed class DisciplineRecord
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private DisciplineRecord()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the division-season the cards were shown in.</summary>
    public Guid DivisionSeasonId { get; private set; }

    /// <summary>Gets the booked player.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the league yellow cards the player has accumulated (`DIS-2`).</summary>
    public int YellowCards { get; private set; }

    /// <summary>Gets the times the player has been sent off (`DIS-4`).</summary>
    public int RedCards { get; private set; }

    /// <summary>Gets when the record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the record was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Opens a player's record for a division-season, with no cards yet.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="divisionSeasonId">The division-season the cards belong to.</param>
    /// <param name="playerId">The booked player.</param>
    /// <param name="now">The current instant.</param>
    public static DisciplineRecord Open(
        Guid id,
        Guid divisionSeasonId,
        Guid playerId,
        DateTimeOffset now)
    {
        if (divisionSeasonId == Guid.Empty)
        {
            throw new ArgumentException("A discipline record belongs to a division-season.", nameof(divisionSeasonId));
        }

        if (playerId == Guid.Empty)
        {
            throw new ArgumentException("A discipline record belongs to a player.", nameof(playerId));
        }

        return new DisciplineRecord
        {
            Id = id,
            DivisionSeasonId = divisionSeasonId,
            PlayerId = playerId,
            YellowCards = 0,
            RedCards = 0,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// How many yellow-accumulation suspensions a set of bookings earns, before the count is advanced
    /// (`DIS-2`).
    /// </summary>
    /// <param name="yellowCards">The yellows shown in the match about to be applied.</param>
    /// <param name="threshold">The accumulation threshold, from the rule set.</param>
    /// <returns>How many thresholds the new bookings cross.</returns>
    /// <remarks>
    /// The comparison is on the running total divided by the threshold, so it counts every multiple the
    /// season reaches — the fifth booking and the tenth each earn one — without the count ever being reset
    /// away from the accumulation `DIS-3` says is only cleared at rollover.
    /// </remarks>
    public int YellowSuspensionsEarned(int yellowCards, int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(yellowCards);
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);

        return ((YellowCards + yellowCards) / threshold) - (YellowCards / threshold);
    }

    /// <summary>Adds the cards a player received in one match.</summary>
    /// <param name="yellowCards">The yellows shown, counting a second yellow as the booking it was.</param>
    /// <param name="redCards">The sending-offs, counting a second yellow as the red it became.</param>
    /// <param name="now">The current instant.</param>
    public void AddCards(int yellowCards, int redCards, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(yellowCards);
        ArgumentOutOfRangeException.ThrowIfNegative(redCards);

        if (yellowCards == 0 && redCards == 0)
        {
            throw new ArgumentException("A discipline record is only advanced by a card.", nameof(yellowCards));
        }

        YellowCards += yellowCards;
        RedCards += redCards;

        UpdatedAt = now;
        Version++;
    }
}

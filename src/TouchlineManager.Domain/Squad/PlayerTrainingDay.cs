using System.Globalization;
using System.Text;
using System.Text.Json;
using TouchlineManager.Domain.Squad.Training;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// One player's training for one progression day: the regime in force and what it did (`TRN-17`).
/// </summary>
/// <remarks>
/// <para>
/// An append-only time series, one row per player per day, written by the daily progression run in the
/// same unit of work as the attribute change it records. The unique <c>(player_id, day)</c> index is a
/// second guard on the run's idempotency: a retried day cannot record twice.
/// </para>
/// <para>
/// The attribute changes are stored as compact JSON, <c>[[attributeIndex, delta], ...]</c> in canonical
/// attribute order, because the history is read whole for charting and never filtered by attribute.
/// </para>
/// </remarks>
public sealed class PlayerTrainingDay
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerTrainingDay()
    {
    }

    /// <summary>Gets the row identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the player trained.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the progression day.</summary>
    public DateOnly Day { get; private set; }

    /// <summary>Gets the programme the player trained that day.</summary>
    public TrainingProgramme Programme { get; private set; }

    /// <summary>Gets the club intensity that day.</summary>
    public TrainingIntensity Intensity { get; private set; }

    /// <summary>Gets the development the day earned, in thousandths of a point.</summary>
    public int DevelopmentMilli { get; private set; }

    /// <summary>Gets the decline the day incurred, in thousandths of a point.</summary>
    public int DeclineMilli { get; private set; }

    /// <summary>Gets the whole points gained across all attributes that day.</summary>
    public int PointsGained { get; private set; }

    /// <summary>Gets the whole points lost across all attributes that day.</summary>
    public int PointsLost { get; private set; }

    /// <summary>Gets the per-attribute changes as compact JSON.</summary>
    public string AttributeChangesJson { get; private set; } = "[]";

    /// <summary>Records one player's day from the calculator's outcome.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="playerId">The player trained.</param>
    /// <param name="day">The progression day.</param>
    /// <param name="programme">The programme in force.</param>
    /// <param name="intensity">The club intensity in force.</param>
    /// <param name="outcome">What the calculator produced for the day.</param>
    public static PlayerTrainingDay Record(
        Guid id,
        Guid playerId,
        DateOnly day,
        TrainingProgramme programme,
        TrainingIntensity intensity,
        DailyProgressionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new PlayerTrainingDay
        {
            Id = id,
            PlayerId = playerId,
            Day = day,
            Programme = programme,
            Intensity = intensity,
            DevelopmentMilli = outcome.DevelopmentMilli,
            DeclineMilli = outcome.DeclineMilli,
            PointsGained = outcome.AttributeChanges.Where(change => change.Delta > 0).Sum(change => change.Delta),
            PointsLost = outcome.AttributeChanges.Where(change => change.Delta < 0).Sum(change => -change.Delta),
            AttributeChangesJson = Serialize(outcome.AttributeChanges),
        };
    }

    /// <summary>Reads the stored per-attribute changes back.</summary>
    public IReadOnlyList<AttributeChange> ParseChanges() => Parse(AttributeChangesJson);

    /// <summary>Renders changes as <c>[[attributeIndex, delta], ...]</c>.</summary>
    /// <param name="changes">The changes, in canonical attribute order.</param>
    public static string Serialize(IReadOnlyList<AttributeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var builder = new StringBuilder("[");

        for (var i = 0; i < changes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder
                .Append('[')
                .Append(((int)changes[i].Attribute).ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(changes[i].Delta.ToString(CultureInfo.InvariantCulture))
                .Append(']');
        }

        return builder.Append(']').ToString();
    }

    /// <summary>Parses the compact form produced by <see cref="Serialize"/>.</summary>
    /// <param name="json">The stored JSON.</param>
    public static IReadOnlyList<AttributeChange> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var pairs = JsonSerializer.Deserialize<int[][]>(json) ?? [];

        return [.. pairs.Select(pair => new AttributeChange((AttributeName)pair[0], pair[1]))];
    }
}

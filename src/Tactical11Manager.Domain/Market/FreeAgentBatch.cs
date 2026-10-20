namespace Tactical11Manager.Domain.Market;

/// <summary>
/// One week's free-agent pool: the record that the batch was opened, and what it held (`FA-1`, `FA-5`).
/// </summary>
/// <remarks>
/// <para>
/// A batch is unique per world and week, which is what makes the weekly job idempotent: a retried or
/// duplicated job finds the row and does nothing, so a week can never be opened twice. The listings belong to
/// the batch rather than to a seller club.
/// </para>
/// <para>
/// <see cref="GemCount"/> is server-only (class C2). It is an audit and measurement figure and is never
/// returned by an API.
/// </para>
/// </remarks>
public sealed class FreeAgentBatch
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private FreeAgentBatch()
    {
    }

    /// <summary>Gets the batch identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning world.</summary>
    public Guid WorldId { get; private set; }

    /// <summary>Gets the Monday the batch opens on (`FA-5`). Unique per world.</summary>
    public DateOnly WeekStart { get; private set; }

    /// <summary>Gets when the batch's listings open.</summary>
    public DateTimeOffset OpensAt { get; private set; }

    /// <summary>Gets when the batch's listings resolve, the next week's opening instant.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Gets how many players were under active contract when the batch was sized (`FA-1`).</summary>
    public int ContractedCount { get; private set; }

    /// <summary>Gets how many players the batch generated.</summary>
    public int GeneratedCount { get; private set; }

    /// <summary>Gets how many released players the batch added on top of the generated ones (`FA-3`).</summary>
    public int ReleasedCount { get; private set; }

    /// <summary>Gets how many generated players are hidden gems. Server-only (class C2).</summary>
    public int GemCount { get; private set; }

    /// <summary>Gets the generator version that produced the batch (`FIC-8`).</summary>
    public string GeneratorVersion { get; private set; } = string.Empty;

    /// <summary>Gets when the batch row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Opens a batch record.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="worldId">The owning world.</param>
    /// <param name="weekStart">The Monday the batch opens on.</param>
    /// <param name="opensAt">When its listings open.</param>
    /// <param name="endsAt">When its listings resolve, which must be after they open.</param>
    /// <param name="contractedCount">The players under active contract when the batch was sized.</param>
    /// <param name="generatedCount">The generated players.</param>
    /// <param name="releasedCount">The released players added on top.</param>
    /// <param name="gemCount">The hidden gems among the generated players.</param>
    /// <param name="generatorVersion">The generator version.</param>
    /// <param name="now">The current instant.</param>
    public static FreeAgentBatch Open(
        Guid id,
        Guid worldId,
        DateOnly weekStart,
        DateTimeOffset opensAt,
        DateTimeOffset endsAt,
        int contractedCount,
        int generatedCount,
        int releasedCount,
        int gemCount,
        string generatorVersion,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(contractedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(generatedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(releasedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(gemCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gemCount, generatedCount);

        if (endsAt <= opensAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAt),
                endsAt,
                "A batch resolves after it opens.");
        }

        return new FreeAgentBatch
        {
            Id = id,
            WorldId = worldId,
            WeekStart = weekStart,
            OpensAt = opensAt,
            EndsAt = endsAt,
            ContractedCount = contractedCount,
            GeneratedCount = generatedCount,
            ReleasedCount = releasedCount,
            GemCount = gemCount,
            GeneratorVersion = generatorVersion,
            CreatedAt = now,
        };
    }
}

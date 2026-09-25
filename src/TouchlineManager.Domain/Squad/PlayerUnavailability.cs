namespace TouchlineManager.Domain.Squad;

/// <summary>What is keeping a player out (`DIS-1`, `DIS-4`).</summary>
public enum UnavailabilityType
{
    /// <summary>An injury.</summary>
    Injury = 0,

    /// <summary>A card suspension.</summary>
    Suspension = 1,
}

/// <summary>
/// How serious an injury is.
/// </summary>
/// <remarks>
/// The band names are here so a record can describe itself; the mapping from a band to a number of
/// fixtures is a discipline rule and arrives with Stage 8, which is what applies injuries. Stage 4
/// creates the table and stores the count the later stage will write.
/// </remarks>
public enum InjurySeverity
{
    /// <summary>The shortest absences.</summary>
    Minor = 0,

    /// <summary>A mid-length absence.</summary>
    Moderate = 1,

    /// <summary>The longest absences.</summary>
    Major = 2,
}

/// <summary>Stable codes and storage representation for unavailability and severity.</summary>
public static class Unavailabilities
{
    /// <summary>The code for <see cref="UnavailabilityType.Injury"/>.</summary>
    public const string InjuryCode = "injury";

    /// <summary>The code for <see cref="UnavailabilityType.Suspension"/>.</summary>
    public const string SuspensionCode = "suspension";

    /// <summary>The code for <see cref="InjurySeverity.Minor"/>.</summary>
    public const string MinorCode = "minor";

    /// <summary>The code for <see cref="InjurySeverity.Moderate"/>.</summary>
    public const string ModerateCode = "moderate";

    /// <summary>The code for <see cref="InjurySeverity.Major"/>.</summary>
    public const string MajorCode = "major";

    /// <summary>The longest type code, so a column can be sized to hold every value.</summary>
    public const int MaxTypeCodeLength = 10;

    /// <summary>The longest severity code, so a column can be sized to hold every value.</summary>
    public const int MaxSeverityCodeLength = 8;

    /// <summary>Converts a type to its stable code.</summary>
    /// <param name="type">The unavailability type.</param>
    public static string ToCode(this UnavailabilityType type) => type switch
    {
        UnavailabilityType.Injury => InjuryCode,
        UnavailabilityType.Suspension => SuspensionCode,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown unavailability type."),
    };

    /// <summary>Parses a stable code back to its type.</summary>
    /// <param name="code">The stable code.</param>
    public static UnavailabilityType TypeFromCode(string code) => code switch
    {
        InjuryCode => UnavailabilityType.Injury,
        SuspensionCode => UnavailabilityType.Suspension,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown unavailability type code."),
    };

    /// <summary>Converts a severity to its stable code.</summary>
    /// <param name="severity">The injury severity.</param>
    public static string ToCode(this InjurySeverity severity) => severity switch
    {
        InjurySeverity.Minor => MinorCode,
        InjurySeverity.Moderate => ModerateCode,
        InjurySeverity.Major => MajorCode,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown injury severity."),
    };

    /// <summary>Parses a stable code back to its severity.</summary>
    /// <param name="code">The stable code.</param>
    public static InjurySeverity SeverityFromCode(string code) => code switch
    {
        MinorCode => InjurySeverity.Minor,
        ModerateCode => InjurySeverity.Moderate,
        MajorCode => InjurySeverity.Major,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown injury severity code."),
    };
}

/// <summary>
/// A record preventing a player's selection, measured in fixtures rather than days (`TRN-12`, `DIS-5`).
/// </summary>
/// <remarks>
/// Fixtures, not wall-clock time, because matches are asynchronous and a player must not become
/// available merely because a weekend passed: a suspension is served against the club's next eligible
/// league fixtures and an outstanding one carries into the next season (`DIS-5`, `DIS-8`).
/// </remarks>
public sealed class PlayerUnavailability
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerUnavailability()
    {
    }

    /// <summary>Gets the record identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the unavailable player.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the club the player was unavailable for.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the kind of unavailability.</summary>
    public UnavailabilityType Type { get; private set; }

    /// <summary>Gets the fixture that caused the record, when one did.</summary>
    public Guid? SourceFixtureId { get; private set; }

    /// <summary>Gets when the record started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets how many eligible fixtures remain before the player is available again.</summary>
    public int RemainingFixtures { get; private set; }

    /// <summary>Gets how serious the injury is. Irrelevant for a suspension.</summary>
    public InjurySeverity Severity { get; private set; }

    /// <summary>Gets when the record was resolved, if it has been.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Gets when the record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the record was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets whether the player is still unavailable.</summary>
    public bool IsOpen => ResolvedAt is null;

    /// <summary>Opens an unavailability record.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="playerId">The unavailable player.</param>
    /// <param name="clubId">The club the player is unavailable for.</param>
    /// <param name="type">The kind of unavailability.</param>
    /// <param name="severity">How serious the injury is.</param>
    /// <param name="remainingFixtures">How many eligible fixtures the player misses.</param>
    /// <param name="sourceFixtureId">The fixture that caused the record, when one did.</param>
    /// <param name="now">The current instant.</param>
    public static PlayerUnavailability Open(
        Guid id,
        Guid playerId,
        Guid clubId,
        UnavailabilityType type,
        InjurySeverity severity,
        int remainingFixtures,
        Guid? sourceFixtureId,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(remainingFixtures, 1);

        return new PlayerUnavailability
        {
            Id = id,
            PlayerId = playerId,
            ClubId = clubId,
            Type = type,
            SourceFixtureId = sourceFixtureId,
            StartedAt = now,
            RemainingFixtures = remainingFixtures,
            Severity = severity,
            ResolvedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Serves one eligible fixture, resolving the record once the remaining count reaches zero.</summary>
    /// <param name="now">The current instant.</param>
    /// <returns>Whether the record resolved as a result of serving this fixture.</returns>
    public bool ServeFixture(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("An unavailability that has already resolved cannot be served.");
        }

        RemainingFixtures--;

        if (RemainingFixtures == 0)
        {
            ResolvedAt = now;
        }

        Touch(now);

        return ResolvedAt is not null;
    }

    /// <summary>Resolves the record early, for an administrative repair.</summary>
    /// <param name="now">The current instant.</param>
    public void Resolve(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("An unavailability cannot be resolved twice.");
        }

        RemainingFixtures = 0;
        ResolvedAt = now;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

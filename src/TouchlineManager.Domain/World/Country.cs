namespace TouchlineManager.Domain.World;

/// <summary>
/// One of the game's countries.
/// </summary>
/// <remarks>
/// <para>
/// A country owns its pyramid: its clubs, its divisions, and its provisioning requests. Display names
/// are the real country names — the fictional-data policy covers <em>clubs</em>, <em>players</em>,
/// <em>competitions</em>, and <em>marks</em>, not the countries themselves (`WORLD-3`).
/// </para>
/// <para>
/// <see cref="NamePoolKey"/> is what makes generated club identity reproducible by locale: the
/// generator selects a name dictionary by this key rather than by guessing from the display name, so
/// adding a country is a data change rather than a branch.
/// </para>
/// </remarks>
public sealed class Country
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Country()
    {
    }

    /// <summary>Gets the country identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning world.</summary>
    public Guid WorldId { get; private set; }

    /// <summary>Gets the stable three-letter code (`WORLD-2`), e.g. <c>ENG</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Gets the display name shown to managers.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Gets the locale used for formatting and, later, localization, e.g. <c>en-GB</c>.</summary>
    public string Locale { get; private set; } = string.Empty;

    /// <summary>Gets the key selecting the fictional name dictionary used to generate its clubs.</summary>
    public string NamePoolKey { get; private set; } = string.Empty;

    /// <summary>Gets the presentation order.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Gets a value indicating whether the country takes part in play.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets when the country was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the country was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Creates a country from a launch definition.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="worldId">The owning world.</param>
    /// <param name="definition">The launch definition supplying code, name, locale, and pool key.</param>
    /// <param name="now">The current instant.</param>
    public static Country Create(Guid id, Guid worldId, LaunchCountry definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new Country
        {
            Id = id,
            WorldId = worldId,
            Code = definition.Code,
            DisplayName = definition.DisplayName,
            Locale = definition.Locale,
            NamePoolKey = definition.NamePoolKey,
            SortOrder = definition.SortOrder,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Takes the country out of play without deleting its history (`PYR-12`).</summary>
    /// <param name="now">The current instant.</param>
    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

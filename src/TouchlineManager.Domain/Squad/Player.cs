using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// A fictional athlete: the unit a manager selects, trains, contracts, and transfers.
/// </summary>
/// <remarks>
/// <para>
/// A player is a persistent record, never deleted (`data-classification.md` §3). Identity is generated
/// rather than entered (`WORLD-3`), and the aggregate owns only the attributes of the person — ability
/// lives in <see cref="PlayerAttributes"/>, condition in <see cref="PlayerState"/>, and the club
/// relationship in <see cref="PlayerContract"/> and <see cref="PlayerRegistration"/>. Splitting them
/// keeps the hot row small and each of the four independently writable.
/// </para>
/// <para>
/// <see cref="Potential"/> and <see cref="Reputation"/> are class C2 hidden values. They live here as
/// server-only columns and are excluded from every DTO; `data-classification.md` §2.1 requires a test
/// that fails if one reaches a manager-facing response.
/// </para>
/// </remarks>
public sealed class Player
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Player()
    {
    }

    /// <summary>Gets the player identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning world.</summary>
    public Guid WorldId { get; private set; }

    /// <summary>Gets the generated full name.</summary>
    public string FullName { get; private set; } = string.Empty;

    /// <summary>Gets the abbreviated name for lineups and tables.</summary>
    public string ShortName { get; private set; } = string.Empty;

    /// <summary>Gets the nationality country code.</summary>
    public string NationalityCode { get; private set; } = string.Empty;

    /// <summary>Gets the stored name seed, so the generated name can be reproduced on its own.</summary>
    public string NameSeed { get; private set; } = string.Empty;

    /// <summary>Gets the game year the player was born in. Age follows the game year, not real time (`TIME-3`).</summary>
    public int BirthGameYear { get; private set; }

    /// <summary>Gets the day of the birth game year, 1–366.</summary>
    public int BirthDayOfYear { get; private set; }

    /// <summary>Gets the foot the player favours.</summary>
    public PreferredFoot PreferredFoot { get; private set; }

    /// <summary>Gets the player's height in centimetres.</summary>
    public int HeightCm { get; private set; }

    /// <summary>Gets the player's weight in kilograms.</summary>
    public int WeightKg { get; private set; }

    /// <summary>Gets the position the player is most at home in.</summary>
    public PlayerPosition PrimaryPosition { get; private set; }

    /// <summary>
    /// Gets the other positions the player can cover, as a stable comma-separated code list.
    /// </summary>
    /// <remarks>
    /// Stored as text rather than as a related table because the set is tiny, ordered, and only ever read
    /// whole with the player (`data-model.md` §3.2). The property is the raw column; use
    /// <see cref="SecondaryPositions"/> for the parsed form.
    /// </remarks>
    public string SecondaryPositionCodes { get; private set; } = string.Empty;

    /// <summary>Gets the other positions the player can cover.</summary>
    public IReadOnlyList<PlayerPosition> SecondaryPositions => PlayerPositions.ParseCodes(SecondaryPositionCodes);

    /// <summary>Gets the lifecycle state.</summary>
    public PlayerStatus Status { get; private set; }

    /// <summary>
    /// Gets the hidden development ceiling (`TRN-9`). Class C2: never serialized to a manager.
    /// </summary>
    public int Potential { get; private set; }

    /// <summary>Gets the hidden generation reputation. Class C2: never serialized to a manager.</summary>
    public int Reputation { get; private set; }

    /// <summary>Gets when the player was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the player was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Generates a player from a generated identity.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="worldId">The owning world.</param>
    /// <param name="identity">The generated name, physique, positions, and hidden values.</param>
    /// <param name="now">The current instant.</param>
    public static Player Generate(Guid id, Guid worldId, PlayerIdentity identity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.FullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.NationalityCode);

        if (identity.BirthDayOfYear is < 1 or > 366)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identity),
                identity.BirthDayOfYear,
                "A birth day of year is between 1 and 366.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(identity.HeightCm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(identity.WeightKg);

        EnsureAttributeScale(identity.Potential, nameof(identity));
        EnsureAttributeScale(identity.Reputation, nameof(identity));

        return new Player
        {
            Id = id,
            WorldId = worldId,
            FullName = identity.FullName.Trim(),
            ShortName = identity.ShortName.Trim(),
            NationalityCode = identity.NationalityCode.Trim().ToUpperInvariant(),
            NameSeed = identity.NameSeed,
            BirthGameYear = identity.BirthGameYear,
            BirthDayOfYear = identity.BirthDayOfYear,
            PreferredFoot = identity.PreferredFoot,
            HeightCm = identity.HeightCm,
            WeightKg = identity.WeightKg,
            PrimaryPosition = identity.PrimaryPosition,
            SecondaryPositionCodes = PlayerPositions.JoinCodes(identity.SecondaryPositions),
            Status = PlayerStatus.Active,
            Potential = identity.Potential,
            Reputation = identity.Reputation,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Gets the player's age in the given game year.</summary>
    /// <param name="gameYear">The game year to measure against, from the season being played.</param>
    public int AgeIn(int gameYear) => gameYear - BirthGameYear;

    /// <summary>Gets whether the player is at home in the given position family, primary or secondary.</summary>
    /// <param name="family">The family to test.</param>
    public bool PlaysIn(PositionFamily family) =>
        PlayerPositions.FamilyOf(PrimaryPosition) == family
        || SecondaryPositions.Any(position => PlayerPositions.FamilyOf(position) == family);

    /// <summary>Retires the player, keeping the record readable.</summary>
    /// <param name="now">The current instant.</param>
    public void Retire(DateTimeOffset now)
    {
        Status = PlayerStatus.Retired;

        Touch(now);
    }

    /// <summary>Releases the player to free agency (`CON-6`).</summary>
    /// <param name="now">The current instant.</param>
    public void ReleaseToFreeAgency(DateTimeOffset now)
    {
        Status = PlayerStatus.FreeAgent;

        Touch(now);
    }

    /// <summary>Removes the person's identity on a data-subject request, retaining the record.</summary>
    /// <param name="now">The current instant.</param>
    public void Anonymize(DateTimeOffset now)
    {
        Status = PlayerStatus.Anonymized;

        Touch(now);
    }

    private static void EnsureAttributeScale(int value, string parameterName)
    {
        if (value is < WorldRuleSet.AttributeMin or > WorldRuleSet.AttributeMax)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"A potential or reputation value is between {WorldRuleSet.AttributeMin} and {WorldRuleSet.AttributeMax} (TRN-4).");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

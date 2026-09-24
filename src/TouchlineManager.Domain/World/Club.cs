using System.Globalization;
using System.Text;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.World;

/// <summary>
/// A football club. The unit of competition, and the thing a manager controls for a while.
/// </summary>
/// <remarks>
/// <para>
/// A club carries no reference to a human manager (`WORLD-7`). Control is derived from the active
/// <see cref="ClubTenure"/>, so a club survives a manager resigning, going inactive, or being closed
/// out by the inactivity ladder without a single write to its own row.
/// </para>
/// <para>
/// Identity is generated, not entered (`WORLD-3`). <see cref="NormalizedName"/> and <see cref="Slug"/>
/// are derived from <see cref="Name"/> rather than supplied, so the unique indexes cannot be defeated
/// by a name and a slug that disagree.
/// </para>
/// </remarks>
public sealed class Club
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Club()
    {
    }

    /// <summary>Gets the club identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning world.</summary>
    public Guid WorldId { get; private set; }

    /// <summary>Gets the country whose pyramid the club belongs to.</summary>
    public Guid CountryId { get; private set; }

    /// <summary>Gets the generated club name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the derived comparison form of <see cref="Name"/>, which carries the unique index.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>Gets the abbreviated name for tables and the match viewer.</summary>
    public string ShortName { get; private set; } = string.Empty;

    /// <summary>Gets the derived URL-safe identifier, which carries the unique index.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Gets the generated home city.</summary>
    public string City { get; private set; } = string.Empty;

    /// <summary>Gets the generated region or state.</summary>
    public string Region { get; private set; } = string.Empty;

    /// <summary>Gets the deterministic seed a procedurally drawn badge is derived from.</summary>
    public string BadgeSeed { get; private set; } = string.Empty;

    /// <summary>Gets the game year the club was founded, which is a game year and not a real one.</summary>
    public int FoundingGameYear { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public ClubStatus Status { get; private set; }

    /// <summary>Gets the fixed stadium baseline used by gate-revenue calculations (`FIN-3`).</summary>
    public long StadiumBaseline { get; private set; }

    /// <summary>Gets the club's reputation, on the same 1–100 scale as manager reputation.</summary>
    public int Reputation { get; private set; }

    /// <summary>Gets when the club was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the club was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Generates a club for a tier, with its finances and reputation scaled to that tier.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="worldId">The owning world.</param>
    /// <param name="countryId">The owning country.</param>
    /// <param name="identity">The generated identity.</param>
    /// <param name="tier">The tier number the club starts in, which sets its baseline scale.</param>
    /// <param name="foundingGameYear">The game year the club is founded in.</param>
    /// <param name="now">The current instant.</param>
    public static Club Generate(
        Guid id,
        Guid worldId,
        Guid countryId,
        ClubIdentity identity,
        int tier,
        int foundingGameYear,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);

        return new Club
        {
            Id = id,
            WorldId = worldId,
            CountryId = countryId,
            Name = identity.Name.Trim(),
            NormalizedName = NormalizeName(identity.Name),
            ShortName = identity.ShortName.Trim(),
            Slug = SlugFrom(identity.Name),
            City = identity.City.Trim(),
            Region = identity.Region.Trim(),
            BadgeSeed = identity.BadgeSeed,
            FoundingGameYear = foundingGameYear,
            Status = ClubStatus.Active,
            StadiumBaseline = WorldRuleSet.OpeningStadiumBaselineForTier(tier),
            Reputation = WorldRuleSet.OpeningReputationForTier(tier),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Gets the comparison form of a club name, which is what uniqueness is enforced on.</summary>
    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Derives a URL-safe slug from a club name.
    /// </summary>
    /// <remarks>
    /// Accented characters are folded to their base letters rather than dropped, so "Atlético" and
    /// "Atletico" produce the same slug and a generated identity cannot collide with itself across
    /// locales. Non-letter, non-digit runs collapse to a single hyphen.
    /// </remarks>
    public static string SlugFrom(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;

                continue;
            }

            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    /// <summary>Retires the club through an audited administrative repair (`WORLD-6`).</summary>
    /// <param name="now">The current instant.</param>
    public void Retire(DateTimeOffset now)
    {
        Status = ClubStatus.Retired;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

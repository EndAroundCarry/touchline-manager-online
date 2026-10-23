using System.Globalization;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Domain.Competition;

/// <summary>
/// One league of one country's pyramid, e.g. "Spain Division 2.1": the first league of tier 2.
/// </summary>
/// <remarks>
/// <para>
/// A division is the durable tier; <see cref="DivisionSeason"/> is that tier's instance in a
/// particular season. Separating them is what lets `PYR-12` hold — a tier whose human occupancy falls
/// is never removed, because nothing about the tier's existence depends on who is in it this season.
/// </para>
/// <para>
/// Division names are generated from the country and the tier number. No real competition name or mark
/// appears anywhere in the game (`WORLD-3`).
/// </para>
/// </remarks>
public sealed class Division
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Division()
    {
    }

    /// <summary>Gets the division identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning country.</summary>
    public Guid CountryId { get; private set; }

    /// <summary>Gets the tier number. Tier 1 is the top of the pyramid (`WORLD-4`).</summary>
    public int TierNumber { get; private set; }

    /// <summary>Gets the league's number within its tier, starting at 1 (the "1" of "Division 2.1").</summary>
    public int LeagueNumber { get; private set; } = 1;

    /// <summary>Gets the division of the tier above that this league feeds, or <see langword="null"/> for the top division.</summary>
    public Guid? ParentDivisionId { get; private set; }

    /// <summary>Gets the generated display name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Gets the lifecycle state.</summary>
    public DivisionStatus Status { get; private set; }

    /// <summary>Gets the season the division was created in.</summary>
    public Guid CreatedSeasonId { get; private set; }

    /// <summary>Gets how many clubs the division holds. Always <see cref="WorldRuleSet.ClubsPerDivision"/> for a new division; an old world may hold 18.</summary>
    public int Capacity { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets a value indicating whether a manager may take over a club in this division.</summary>
    public bool IsClaimable => DivisionStatusRules.IsClaimable(Status);

    /// <summary>Creates a division in the not-yet-claimable <see cref="DivisionStatus.Provisioning"/> state.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="countryId">The owning country.</param>
    /// <param name="tierNumber">The tier number, starting at 1.</param>
    /// <param name="leagueNumber">The league's number within its tier, starting at 1.</param>
    /// <param name="parentDivisionId">The division of the tier above that this league feeds; <see langword="null"/> for the top division.</param>
    /// <param name="countryDisplayName">The country's display name, used to build the league name.</param>
    /// <param name="createdSeasonId">The season the division is created in.</param>
    /// <param name="now">The current instant.</param>
    public static Division Provision(
        Guid id,
        Guid countryId,
        int tierNumber,
        int leagueNumber,
        Guid? parentDivisionId,
        string countryDisplayName,
        Guid createdSeasonId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryDisplayName);

        if (tierNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tierNumber), tierNumber, "A tier starts at 1 (WORLD-4).");
        }

        if (leagueNumber < 1 || leagueNumber > PyramidShape.LeaguesAllowed(tierNumber))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leagueNumber),
                leagueNumber,
                "A tier holds 2^(tier-1) leagues, numbered from 1 (PYR-15).");
        }

        if (tierNumber == 1 && parentDivisionId is not null)
        {
            throw new ArgumentException("The top division feeds no other league (PYR-15).", nameof(parentDivisionId));
        }

        return new Division
        {
            Id = id,
            CountryId = countryId,
            TierNumber = tierNumber,
            LeagueNumber = leagueNumber,
            ParentDivisionId = parentDivisionId,
            DisplayName = NameFor(countryDisplayName, tierNumber, leagueNumber),
            Status = DivisionStatus.Provisioning,
            CreatedSeasonId = createdSeasonId,
            Capacity = WorldRuleSet.ClubsPerDivision,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Builds a generic, unbranded league name for a country.
    /// </summary>
    /// <remarks>
    /// "Top Division" and "Division N.L" are deliberately descriptive rather than evocative of any real
    /// competition, so no protected league branding can leak in through a generated name (`WORLD-3`). Every
    /// league below the top carries its number within the tier, even while it is alone in it ("Division 2.1").
    /// </remarks>
    /// <param name="countryDisplayName">The country's display name.</param>
    /// <param name="tierNumber">The tier number.</param>
    /// <param name="leagueNumber">The league's number within its tier.</param>
    public static string NameFor(string countryDisplayName, int tierNumber, int leagueNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryDisplayName);

        return tierNumber == 1
            ? string.Create(CultureInfo.InvariantCulture, $"{countryDisplayName.Trim()} Top Division")
            : string.Create(CultureInfo.InvariantCulture, $"{countryDisplayName.Trim()} Division {tierNumber}.{leagueNumber}");
    }

    /// <summary>Marks the division complete so its clubs become claimable (`PYR-8`).</summary>
    /// <param name="now">The current instant.</param>
    public void Activate(DateTimeOffset now)
    {
        if (Status == DivisionStatus.Retired)
        {
            throw new InvalidOperationException("A retired division cannot be activated.");
        }

        Status = DivisionStatus.Active;

        Touch(now);
    }

    /// <summary>
    /// Retires the division through an audited administrative repair.
    /// </summary>
    /// <remarks>
    /// Retirement is monotonic in the same way provisioning is (`PYR-12`): a retired tier is never
    /// brought back, because the historical seasons that reference it must keep resolving.
    /// </remarks>
    /// <param name="now">The current instant.</param>
    public void Retire(DateTimeOffset now)
    {
        Status = DivisionStatus.Retired;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

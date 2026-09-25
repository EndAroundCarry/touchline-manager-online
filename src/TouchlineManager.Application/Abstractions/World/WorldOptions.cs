using System.ComponentModel.DataAnnotations;

namespace TouchlineManager.Application.Abstractions.World;

/// <summary>
/// Configuration for the world and onboarding (master plan §10.2, `CAL-5`).
/// </summary>
/// <remarks>
/// Validated at startup, so a misconfigured deployment fails immediately rather than the first time a
/// manager tries to onboard.
/// </remarks>
public sealed class WorldOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "World";

    /// <summary>Gets or sets the world's display name.</summary>
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = "Touchline Manager World";

    /// <summary>
    /// Gets or sets the default seed the world seeder uses when none is supplied (`PYR-14`).
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string GenerationSeed { get; set; } = "touchline-world-1";

    /// <summary>
    /// Gets or sets the date the first season is intended to begin on (`CAL-5`).
    /// </summary>
    /// <remarks>
    /// A configured date, never derived from deployment time: a world seeded by a deploy must not start
    /// its season on the day the deploy happened. The seeder rounds this forward to the next legal
    /// matchday, so an operator may name any date rather than having to work out the weekday.
    /// </remarks>
    public DateOnly FirstSeasonStartDate { get; set; } = new(2026, 10, 6);

    /// <summary>
    /// Gets or sets how long a client should wait before re-asking whether the next tier is ready
    /// (`PYR-10`).
    /// </summary>
    [Range(5, 3600)]
    public int ProvisioningPollSeconds { get; set; } = 30;
}

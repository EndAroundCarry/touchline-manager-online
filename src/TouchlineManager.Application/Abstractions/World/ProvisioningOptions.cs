using System.ComponentModel.DataAnnotations;

namespace TouchlineManager.Application.Abstractions.World;

/// <summary>
/// Configuration for the provisioning worker (`PYR-4`, ADR-0005).
/// </summary>
/// <remarks>
/// Bound and validated like the other schedulers' options, so a misconfigured interval fails at startup
/// rather than the first time the materialiser ticks. <see cref="EnableProvisioning"/> lets an operator pause
/// pyramid growth without stopping the season, e.g. while a provisioning defect is investigated.
/// </remarks>
public sealed class ProvisioningOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Provisioning";

    /// <summary>Gets or sets whether the worker materialises provisioning jobs. On by default.</summary>
    public bool EnableProvisioning { get; set; } = true;

    /// <summary>Gets or sets how often the materialiser looks for pending requests, in seconds.</summary>
    [Range(30, 86_400)]
    public int CheckIntervalSeconds { get; set; } = 60;
}

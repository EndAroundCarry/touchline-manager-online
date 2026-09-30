using System.Diagnostics.Metrics;
using TouchlineManager.Application.Abstractions.Ops;

// The interface method is named OnboardingStep, which shadows the enum of the same name inside this class;
// the alias keeps the enum's members reachable.
using OnboardingStepKind = TouchlineManager.Application.Abstractions.Ops.OnboardingStep;

namespace TouchlineManager.Infrastructure.Telemetry;

/// <summary>
/// The operational funnel counters, recorded on the shared OpenTelemetry meter (master plan §14.1, `F-54`,
/// ADR-0041).
/// </summary>
/// <remarks>
/// <para>
/// A count and a step name, and nothing else. There is no identity, no value, and no per-manager dimension
/// on any instrument — "counts and durations only, never values" (`docs/security/data-classification.md`
/// §2). The meter is registered by both composition roots, so a collector sees the same instruments from
/// the API and the worker when one is configured; with no collector the calls are a no-op.
/// </para>
/// <para>
/// The counters are the live rate of the same transitions the operator query reads as a snapshot:
/// onboarding is counted as a step, tenure change as an event with its reason.
/// </para>
/// </remarks>
public sealed class OperationalMetrics : IOperationalMetrics, IDisposable
{
    /// <summary>The meter's name, registered with the OpenTelemetry pipeline by each host.</summary>
    public const string MeterName = "TouchlineManager.Analytics";

    private readonly Meter _meter;
    private readonly Counter<long> _onboarding;
    private readonly Counter<long> _tenure;

    /// <summary>Creates the meter and its instruments.</summary>
    public OperationalMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _onboarding = _meter.CreateCounter<long>(
            "touchline.analytics.onboarding",
            unit: "{account}",
            description: "Accounts that reached an onboarding step.");

        _tenure = _meter.CreateCounter<long>(
            "touchline.analytics.tenure",
            unit: "{tenure}",
            description: "Club tenures that became inactive or closed, by reason.");
    }

    /// <inheritdoc />
    public void OnboardingStep(OnboardingStepKind onboardingStep) =>
        _onboarding.Add(1, new KeyValuePair<string, object?>("step", StepCode(onboardingStep)));

    /// <inheritdoc />
    public void TenureBecameInactive() =>
        _tenure.Add(1, new KeyValuePair<string, object?>("event", InactiveEvent));

    /// <inheritdoc />
    public void TenureClosed(string reason) =>
        _tenure.Add(
            1,
            new KeyValuePair<string, object?>("event", ClosedEvent),
            new KeyValuePair<string, object?>("reason", reason));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>The tag value for an onboarding step, shared with the tests that pin the vocabulary.</summary>
    /// <param name="onboardingStep">The step to name.</param>
    public static string StepCode(OnboardingStepKind onboardingStep) => onboardingStep switch
    {
        OnboardingStepKind.Registered => "registered",
        OnboardingStepKind.Verified => "verified",
        OnboardingStepKind.ProfileCreated => "profile_created",
        OnboardingStepKind.ClubClaimed => "club_claimed",
        _ => throw new ArgumentOutOfRangeException(nameof(onboardingStep), onboardingStep, "Unknown onboarding step."),
    };

    /// <summary>The tag value for a tenure that became inactive.</summary>
    public const string InactiveEvent = "inactive";

    /// <summary>The tag value for a tenure that closed.</summary>
    public const string ClosedEvent = "closed";
}

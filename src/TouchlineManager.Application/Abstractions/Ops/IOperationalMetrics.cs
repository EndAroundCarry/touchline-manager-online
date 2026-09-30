namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>One step of the onboarding funnel (master plan §15, `F-54`, ADR-0041).</summary>
/// <remarks>
/// The steps are ordered as a manager walks them, so a counter written here reads against the same funnel
/// the operator query computes from the rows.
/// </remarks>
public enum OnboardingStep
{
    /// <summary>An account registered.</summary>
    Registered = 0,

    /// <summary>An account confirmed its email address.</summary>
    Verified = 1,

    /// <summary>A manager profile was created.</summary>
    ProfileCreated = 2,

    /// <summary>A manager took over a club.</summary>
    ClubClaimed = 3,
}

/// <summary>
/// Records the operational funnel transitions as they happen (master plan §14.1, `F-54`, ADR-0041).
/// </summary>
/// <remarks>
/// <para>
/// A port so the application layer stays free of the metrics framework (`DEP-3`): the implementation
/// underneath is <c>System.Diagnostics.Metrics</c>, which the OpenTelemetry pipeline already carries. That
/// is what gives the telemetry backend a rate signal for the same steps the operator query reads as a
/// snapshot.
/// </para>
/// <para>
/// Every method records a count and nothing else. No identity, no value, and no per-manager dimension
/// reaches a metric: "counts and durations only, never values" (`docs/security/data-classification.md` §2).
/// A call is a no-op when the host has no meter listener, which is the case in tests and local runs.
/// </para>
/// </remarks>
public interface IOperationalMetrics
{
    /// <summary>Records that one account reached an onboarding step.</summary>
    /// <param name="onboardingStep">The step reached.</param>
    void OnboardingStep(OnboardingStep onboardingStep);

    /// <summary>Records that one tenure became inactive, handing routine decisions to the AI (`OCC-2`).</summary>
    void TenureBecameInactive();

    /// <summary>Records that one tenure closed.</summary>
    /// <param name="reason">Why it closed, one of <c>ClubTenureEndReasons</c>.</param>
    void TenureClosed(string reason);
}

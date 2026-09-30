using System.Diagnostics.Metrics;
using FluentAssertions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Telemetry;

namespace TouchlineManager.Infrastructure.Tests.Ops;

/// <summary>
/// The operational funnel counters and their vocabulary (master plan §14.1, `F-54`, ADR-0041).
/// </summary>
/// <remarks>
/// The instrument names and tag values are a contract with the Stage 14 dashboards, so they are pinned
/// here rather than left to a reviewer to notice a rename. Nothing about a measurement may carry a value;
/// the assertions read only names, counts, and step/event tags.
/// </remarks>
public sealed class OperationalMetricsTests
{
    [Fact]
    public void An_onboarding_step_is_counted_with_its_step_as_the_only_tag()
    {
        var measurements = new List<Measurement>();

        using var listener = Listen(measurements);
        using var metrics = new OperationalMetrics();

        metrics.OnboardingStep(OnboardingStep.Registered);

        measurements.Should().ContainSingle();

        var measurement = measurements[0];

        measurement.Instrument.Should().Be("touchline.analytics.onboarding");
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().ContainSingle();
        measurement.Tags["step"].Should().Be("registered");
    }

    [Fact]
    public void A_tenure_becoming_inactive_and_closing_are_tagged_by_event_and_reason()
    {
        var measurements = new List<Measurement>();

        using var listener = Listen(measurements);
        using var metrics = new OperationalMetrics();

        metrics.TenureBecameInactive();
        metrics.TenureClosed(ClubTenureEndReasons.Resigned);

        measurements.Should().HaveCount(2);

        measurements[0].Instrument.Should().Be("touchline.analytics.tenure");
        measurements[0].Value.Should().Be(1);
        measurements[0].Tags["event"].Should().Be("inactive");
        measurements[0].Tags.Should().NotContainKey("reason", "an inactivity is not a closure");

        measurements[1].Instrument.Should().Be("touchline.analytics.tenure");
        measurements[1].Tags["event"].Should().Be("closed");
        measurements[1].Tags["reason"].Should().Be(ClubTenureEndReasons.Resigned);
    }

    [Fact]
    public void Every_onboarding_step_has_a_stable_code()
    {
        OperationalMetrics.StepCode(OnboardingStep.Registered).Should().Be("registered");
        OperationalMetrics.StepCode(OnboardingStep.Verified).Should().Be("verified");
        OperationalMetrics.StepCode(OnboardingStep.ProfileCreated).Should().Be("profile_created");
        OperationalMetrics.StepCode(OnboardingStep.ClubClaimed).Should().Be("club_claimed");
    }

    /// <summary>Subscribes to the meter and collects every measurement as a plain value.</summary>
    private static MeterListener Listen(List<Measurement> measurements)
    {
        var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, published) =>
        {
            if (instrument.Meter.Name == OperationalMetrics.MeterName)
            {
                published.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var captured = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                captured[tag.Key] = tag.Value;
            }

            measurements.Add(new Measurement(instrument.Name, value, captured));
        });

        listener.Start();

        return listener;
    }

    /// <summary>One recorded measurement, materialized so it can be asserted after the callback returns.</summary>
    private sealed record Measurement(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags);
}

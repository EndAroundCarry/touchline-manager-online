using FluentAssertions;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Domain.Tests;

/// <summary>
/// The retry schedule decides how fast a failed deadline recovers and how hard a batch of failures
/// hits the database, so it is pinned rather than left to inspection.
/// </summary>
public sealed class JobRetryPolicyTests
{
    [Fact]
    public void First_retry_uses_the_base_delay()
    {
        JobRetryPolicy.NextDelay(attemptCount: 1, jitterSample: 1.0).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Jitter_never_takes_a_delay_below_half_of_the_exponential_value()
    {
        JobRetryPolicy.NextDelay(attemptCount: 1, jitterSample: 0.0).Should().Be(TimeSpan.FromSeconds(15));
        JobRetryPolicy.NextDelay(attemptCount: 1, jitterSample: 0.5).Should().Be(TimeSpan.FromSeconds(22.5));
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 960)]
    [InlineData(7, 1800)]
    public void Delay_doubles_each_attempt_and_then_caps(int attemptCount, int expectedSeconds)
    {
        JobRetryPolicy.NextDelay(attemptCount, jitterSample: 1.0).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Delay_is_capped_at_the_maximum()
    {
        JobRetryPolicy.NextDelay(attemptCount: 50, jitterSample: 1.0).Should().Be(JobRetryPolicy.MaxDelay);
    }

    [Fact]
    public void Delay_never_exceeds_the_cap_for_any_jitter_sample()
    {
        for (var attempt = 1; attempt <= 40; attempt++)
        {
            JobRetryPolicy.NextDelay(attempt, jitterSample: 1.0).Should().BeLessThanOrEqualTo(JobRetryPolicy.MaxDelay);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Attempt_count_below_one_is_rejected(int attemptCount)
    {
        var act = () => JobRetryPolicy.NextDelay(attemptCount, jitterSample: 0.5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Jitter_sample_outside_zero_to_one_is_rejected(double jitterSample)
    {
        var act = () => JobRetryPolicy.NextDelay(attemptCount: 1, jitterSample);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Job_is_permanent_only_once_attempts_are_exhausted()
    {
        JobRetryPolicy.IsPermanentFailure(attemptCount: 7, maxAttempts: 8).Should().BeFalse();
        JobRetryPolicy.IsPermanentFailure(attemptCount: 8, maxAttempts: 8).Should().BeTrue();
        JobRetryPolicy.IsPermanentFailure(attemptCount: 9, maxAttempts: 8).Should().BeTrue();
    }

    [Fact]
    public void A_single_attempt_budget_dead_letters_immediately()
    {
        JobRetryPolicy.IsPermanentFailure(attemptCount: 1, maxAttempts: 1).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Max_attempts_below_one_is_rejected(int maxAttempts)
    {
        var act = () => JobRetryPolicy.IsPermanentFailure(attemptCount: 1, maxAttempts);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

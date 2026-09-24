using FluentAssertions;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Domain.Tests.Auth;

/// <summary>
/// The progressive lockout ladder (ADR-0002). Pure and clock-free, so every escalation step is
/// asserted without waiting for real time.
/// </summary>
public sealed class AccountLockoutPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Below_the_threshold_no_lockout_is_applied(int failedAttempts)
    {
        AccountLockoutPolicy.LockoutDurationFor(failedAttempts).Should().BeNull();
        AccountLockoutPolicy.IsLocked(null, Now).Should().BeFalse();
    }

    [Fact]
    public void Reaching_the_threshold_applies_the_shortest_lockout()
    {
        AccountLockoutPolicy.LockoutDurationFor(AccountLockoutPolicy.MaxFailedAttempts)
            .Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Each_further_failure_escalates_the_lockout()
    {
        AccountLockoutPolicy.LockoutDurationFor(6).Should().Be(TimeSpan.FromMinutes(5));
        AccountLockoutPolicy.LockoutDurationFor(7).Should().Be(TimeSpan.FromMinutes(15));
        AccountLockoutPolicy.LockoutDurationFor(8).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void The_ladder_is_capped_rather_than_growing_without_bound()
    {
        // An unbounded ladder would let an attacker lock a known account out of the game forever.
        AccountLockoutPolicy.LockoutDurationFor(50).Should().Be(AccountLockoutPolicy.MaxLockoutDuration);
        AccountLockoutPolicy.LockoutDurationFor(1_000).Should().Be(AccountLockoutPolicy.MaxLockoutDuration);
    }

    [Fact]
    public void A_lockout_in_the_future_is_in_force()
    {
        AccountLockoutPolicy.IsLocked(Now.AddMinutes(1), Now).Should().BeTrue();
    }

    [Fact]
    public void A_lockout_in_the_past_has_expired()
    {
        AccountLockoutPolicy.IsLocked(Now.AddMinutes(-1), Now).Should().BeFalse();
    }

    [Fact]
    public void Rejecting_a_negative_failure_count_is_a_programming_error()
    {
        var act = () => AccountLockoutPolicy.LockoutDurationFor(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

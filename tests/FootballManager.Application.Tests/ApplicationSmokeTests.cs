using FootballManager.Application.Ops;
using FootballManager.Application.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FootballManager.Application.Tests;

public class NoOpJobHandlerTests
{
    [Fact]
    public void JobType_is_the_stable_noop_type()
    {
        var handler = new NoOpJobHandler(NullLogger<NoOpJobHandler>.Instance);
        handler.JobType.Should().Be("ops.noop");
    }

    [Fact]
    public async Task HandleAsync_completes_without_error()
    {
        var handler = new NoOpJobHandler(NullLogger<NoOpJobHandler>.Instance);
        var context = new JobContext(Guid.NewGuid(), "ops.noop", "test-key", null, 1);

        await handler.HandleAsync(context, CancellationToken.None);
    }
}

public class SystemClockTests
{
    [Fact]
    public void UtcNow_has_no_offset_drift()
    {
        var clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;
        var now = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        now.Offset.Should().Be(TimeSpan.Zero);
        now.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}

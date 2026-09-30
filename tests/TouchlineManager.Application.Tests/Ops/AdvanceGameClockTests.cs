using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Application.Tests.Ops;

/// <summary>
/// The worker's half of a clock step: it writes the target instant and asks every materialiser for the day's
/// jobs (ADR-0049). The endpoint resolves the target; this is what applies it.
/// </summary>
public sealed class AdvanceGameClockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 18, 25, 0, TimeSpan.Zero);

    [Fact]
    public void A_payload_round_trips_the_target_instant()
    {
        var target = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

        ClockJobPayload.TryReadTargetInstant(ClockJobPayload.For(target), out var read).Should().BeTrue();
        read.Should().Be(target);
    }

    [Fact]
    public void A_payload_without_a_target_is_rejected()
    {
        ClockJobPayload.TryReadTargetInstant("{}", out _).Should().BeFalse();
        ClockJobPayload.TryReadTargetInstant("not json", out _).Should().BeFalse();
        ClockJobPayload.TryReadTargetInstant(string.Empty, out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_handler_writes_the_target_and_materialises_every_domain()
    {
        var store = new FakeGameClockStore(Now);
        var first = new RecordingMaterializer();
        var second = new RecordingMaterializer();
        var target = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

        var handler = new AdvanceGameClockJobHandler(
            store,
            [first, second],
            NullLogger<AdvanceGameClockJobHandler>.Instance);

        await handler.HandleAsync(JobFor(target), CancellationToken.None);

        store.Written.Should().ContainSingle().Which.Should().Be(target);
        first.Materialized.Should().ContainSingle().Which.Should().Be(target);
        second.Materialized.Should().ContainSingle().Which.Should().Be(target);
        handler.JobType.Should().Be(ClockJobTypes.Advance);
    }

    [Fact]
    public async Task The_handler_never_moves_the_clock_backwards()
    {
        var store = new FakeGameClockStore(Now);
        var materializer = new RecordingMaterializer();

        var handler = new AdvanceGameClockJobHandler(
            store,
            [materializer],
            NullLogger<AdvanceGameClockJobHandler>.Instance);

        // A target already passed (a missed round, say) is applied at the current instant, so its jobs are
        // materialised due now rather than the clock going back.
        await handler.HandleAsync(JobFor(Now.AddDays(-3)), CancellationToken.None);

        store.Written.Should().ContainSingle().Which.Should().Be(Now);
        materializer.Materialized.Should().ContainSingle().Which.Should().Be(Now);
    }

    [Fact]
    public async Task The_handler_dead_letters_a_payload_with_no_target()
    {
        var handler = new AdvanceGameClockJobHandler(
            new FakeGameClockStore(Now),
            [],
            NullLogger<AdvanceGameClockJobHandler>.Instance);

        var job = new LeasedJob(Guid.CreateVersion7(), ClockJobTypes.Advance, "clock:x", "{}", 1, 8);

        var act = () => handler.HandleAsync(job, CancellationToken.None);

        await act.Should().ThrowAsync<PermanentJobFailureException>();
    }

    private static LeasedJob JobFor(DateTimeOffset target) => new(
        Guid.CreateVersion7(),
        ClockJobTypes.Advance,
        ClockJobTypes.AdvanceKey("day", target),
        ClockJobPayload.For(target),
        1,
        8);

    private sealed class FakeGameClockStore(DateTimeOffset now) : IGameClockStore
    {
        public List<DateTimeOffset> Written { get; } = [];

        public DateTimeOffset Current => Written.Count > 0 ? Written[^1] : now;

        public Task<DateTimeOffset> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task WriteAsync(DateTimeOffset gameNow, CancellationToken cancellationToken)
        {
            Written.Add(gameNow);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMaterializer : IJobMaterializer
    {
        public List<DateTimeOffset> Materialized { get; } = [];

        public Task MaterializeAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            Materialized.Add(now);
            return Task.CompletedTask;
        }
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Application.Tests;

/// <summary>
/// The handler registry is the contract between the queue and the code that runs a deadline. Two
/// handlers claiming one job type would otherwise surface as silently ignored work.
/// </summary>
public sealed class JobHandlerRegistryTests
{
    [Fact]
    public void Registry_resolves_a_registered_handler_by_job_type()
    {
        var handler = new StubHandler("ops.test");
        var registry = new JobHandlerRegistry([handler]);

        registry.Count.Should().Be(1);
        registry.TryGet("ops.test", out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(handler);
    }

    [Fact]
    public void Registry_reports_unknown_job_types_instead_of_throwing()
    {
        var registry = new JobHandlerRegistry([new StubHandler("ops.test")]);

        registry.TryGet("ops.unknown", out var resolved).Should().BeFalse();
        resolved.Should().BeNull();
    }

    [Fact]
    public void Registry_ignores_case_differences_when_resolving()
    {
        var registry = new JobHandlerRegistry([new StubHandler("ops.test")]);

        registry.TryGet("OPS.TEST", out _).Should().BeFalse();
    }

    [Fact]
    public void Two_handlers_for_one_job_type_fail_at_construction()
    {
        var act = () => new JobHandlerRegistry([new StubHandler("ops.duplicate"), new StubHandler("ops.duplicate")]);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*ops.duplicate*");
    }

    [Fact]
    public void Empty_registry_is_valid()
    {
        var registry = new JobHandlerRegistry([]);

        registry.Count.Should().Be(0);
        registry.JobTypes.Should().BeEmpty();
    }

    [Fact]
    public void Registry_rejects_a_null_handler_collection()
    {
        var act = () => new JobHandlerRegistry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task No_op_use_case_derives_its_business_key_from_the_domain_suffix()
    {
        var queue = new RecordingJobQueue();
        var useCase = new EnqueueNoOpJob(new FixedClock(), queue);

        var (businessKey, enqueued) = await useCase.ExecuteAsync("smoke-1", CancellationToken.None);

        businessKey.Should().Be("ops.noop:smoke-1");
        enqueued.Should().BeTrue();
        queue.Requests.Should().ContainSingle();
        queue.Requests[0].JobType.Should().Be(NoOpJobHandler.TypeName);
        queue.Requests[0].BusinessKey.Should().Be(businessKey);
    }

    private sealed class StubHandler(string jobType) : IJobHandler
    {
        public string JobType { get; } = jobType;

        public Task HandleAsync(LeasedJob job, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingJobQueue : IJobQueue
    {
        public List<JobEnqueueRequest> Requests { get; } = [];

        public Task<bool> EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<LeasedJob>> ClaimAsync(
            string leaseOwner,
            int maxJobs,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LeasedJob>>([]);

        public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FailAsync(Guid jobId, string errorMessage, Domain.Ops.JobFailureKind kind, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

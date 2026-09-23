using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// Verifies the Stage 1 walking skeleton end to end: a request enqueues a durable job, the row lands
/// in <c>ops.jobs</c>, and the probe is unreachable unless it is switched on.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpsJobProbeTests
{
    private const string ProbePath = "/api/v1/ops/diagnostics/noop-job";

    private readonly ApiFixture _fixture;

    public OpsJobProbeTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Probe_enqueues_a_durable_job()
    {
        using var client = _fixture.Factory.CreateClient();
        var key = $"probe-{Guid.NewGuid():N}";

        var response = await client.PostAsync($"{ProbePath}?key={key}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<ProbeResponse>();

        body.Should().NotBeNull();
        body!.BusinessKey.Should().Be($"ops.noop:{key}");
        body.Enqueued.Should().BeTrue();

        var job = await FindJobAsync(body.BusinessKey);

        job.Should().NotBeNull();
        job!.JobType.Should().Be("ops.noop");
        job.Status.Should().Be("pending", "the worker, not the API, executes it");
        job.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task Repeating_the_same_key_does_not_enqueue_a_second_job()
    {
        using var client = _fixture.Factory.CreateClient();
        var key = $"probe-idempotent-{Guid.NewGuid():N}";

        var first = await client.PostAsync($"{ProbePath}?key={key}", content: null);
        var second = await client.PostAsync($"{ProbePath}?key={key}", content: null);

        (await first.Content.ReadFromJsonAsync<ProbeResponse>())!.Enqueued.Should().BeTrue();
        (await second.Content.ReadFromJsonAsync<ProbeResponse>())!.Enqueued.Should().BeFalse();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        (await db.Jobs.CountAsync(j => j.BusinessKey == $"ops.noop:{key}")).Should().Be(1);
    }

    [Fact]
    public async Task Probe_is_unreachable_when_the_diagnostics_flag_is_off()
    {
        // Feature-incomplete surfaces must be inaccessible in production (master plan §17.12), so the
        // probe is gated by configuration rather than by convention.
        await using var factory = _fixture.CreateFactory(enableJobProbe: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"{ProbePath}?key=should-not-exist", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unimplemented_modules_are_unreachable()
    {
        // Stage 1 creates the module route groups but attaches no endpoints to them. If this test
        // starts failing, an unfinished module has become reachable.
        using var client = _fixture.Factory.CreateClient();

        foreach (var module in new[] { "auth", "world", "squad", "competition", "match", "market", "finance", "comms" })
        {
            var response = await client.GetAsync($"/api/v1/{module}/anything");

            response.StatusCode.Should().Be(
                HttpStatusCode.NotFound,
                $"'{module}' must not expose endpoints until its stage lands");
        }
    }

    private async Task<JobSnapshot?> FindJobAsync(string businessKey)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await db.Jobs
            .Where(job => job.BusinessKey == businessKey)
            .Select(job => new JobSnapshot(job.JobType, job.Status, job.AttemptCount))
            .SingleOrDefaultAsync();
    }

    private sealed record ProbeResponse(string BusinessKey, bool Enqueued);

    private sealed record JobSnapshot(string JobType, string Status, int AttemptCount);
}

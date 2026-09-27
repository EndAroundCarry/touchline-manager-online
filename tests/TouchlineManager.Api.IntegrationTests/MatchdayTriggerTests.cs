using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The Stage 7 matchday trigger over HTTP: a non-production diagnostic that enqueues a round's real lock
/// and resolution jobs, so a browser journey can watch a real matchday play (ADR-0016).
/// </summary>
/// <remarks>
/// <para>
/// The worker is not started in these tests and must not be: the assertion is that the trigger enqueues
/// exactly the jobs the calendar would — the same business keys, the same types — and that the worker, not
/// the API, would execute them. That is what keeps the journey honest through the real queue.
/// </para>
/// <para>
/// The match read fixture owns a playable world of its own, so a round exists to trigger without
/// disturbing the other API tests' freshly seeded calendar.
/// </para>
/// </remarks>
[Collection(MatchApiCollection.Name)]
public sealed class MatchdayTriggerTests
{
    private const string TriggerPath = "/api/v1/ops/diagnostics/play-matchday";

    private readonly MatchApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public MatchdayTriggerTests(MatchApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_trigger_enqueues_the_rounds_lock_and_resolution_jobs()
    {
        using var client = _fixture.CreateClient();
        var matchdayId = await NextPendingMatchdayAsync();

        var response = await client.PostAsJsonAsync(TriggerPath, new { MatchdayId = matchdayId });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<TriggerResponse>();

        body.Should().NotBeNull();
        body!.MatchdayId.Should().Be(matchdayId);
        body.LockEnqueued.Should().BeTrue();
        body.ResolveEnqueued.Should().BeTrue();
        body.LockKey.Should().Be($"matchday:{matchdayId:D}:lock");
        body.ResolveKey.Should().Be($"matchday:{matchdayId:D}:resolve");

        var jobs = await JobsForAsync(matchdayId);

        jobs.Should().HaveCount(2, "a triggered round needs a lock and a resolution, and nothing else");
        jobs.Select(job => job.JobType).Should().BeEquivalentTo(
        [
            "competition.lock-matchday",
            "competition.resolve-matchday",
        ]);
        jobs.Should().OnlyContain(job => job.Status == "pending", "the worker, not the API, executes them");
    }

    [Fact]
    public async Task Repeating_the_trigger_does_not_enqueue_second_jobs()
    {
        using var client = _fixture.CreateClient();
        var matchdayId = await NextPendingMatchdayAsync();

        await client.PostAsJsonAsync(TriggerPath, new { MatchdayId = matchdayId });
        var second = await client.PostAsJsonAsync(TriggerPath, new { MatchdayId = matchdayId });

        var body = await second.Content.ReadFromJsonAsync<TriggerResponse>();

        body!.LockEnqueued.Should().BeFalse();
        body.ResolveEnqueued.Should().BeFalse();
        (await JobsForAsync(matchdayId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task The_trigger_is_unreachable_when_the_flag_is_off()
    {
        // A non-production surface must be inaccessible in production (master plan §17.12), so it is gated
        // by configuration rather than by convention.
        await using var factory = _fixture.CreateFactory(enableMatchdayTrigger: false);
        using var client = factory.CreateClient();
        var matchdayId = await NextPendingMatchdayAsync();

        var response = await client.PostAsJsonAsync(TriggerPath, new { MatchdayId = matchdayId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_matchday_is_refused_with_a_stable_code()
    {
        using var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            TriggerPath,
            new { MatchdayId = Guid.CreateVersion7() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be("MATCHDAY_NOT_FOUND");
    }

    /// <summary>Finds a round of the seeded season that has not been played yet.</summary>
    private async Task<Guid> NextPendingMatchdayAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await db.Matchdays
            .Where(matchday => matchday.PublicationStatus == MatchdayPublicationStatus.Pending)
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstAsync();
    }

    /// <summary>Reads the durable jobs a round was given.</summary>
    private async Task<IReadOnlyList<JobRow>> JobsForAsync(Guid matchdayId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var prefix = $"matchday:{matchdayId:D}";

        return await db.Jobs
            .Where(job => job.BusinessKey.StartsWith(prefix))
            .Select(job => new JobRow(job.JobType, job.Status))
            .ToListAsync();
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record TriggerResponse(
        Guid MatchdayId,
        string LockKey,
        string ResolveKey,
        bool LockEnqueued,
        bool ResolveEnqueued);

    private sealed record JobRow(string JobType, string Status);
}

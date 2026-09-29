using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The Stage 12 operator rollover controls over HTTP: a non-production diagnostics surface, mapped only when
/// its flag is on (`Diagnostics:EnableRolloverTrigger`, master plan §17.12, ADR-0034).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RolloverControlsTests : IAsyncLifetime
{
    private const string PreviewPath = "/api/v1/ops/diagnostics/preview-rollover";
    private const string RunPath = "/api/v1/ops/diagnostics/run-rollover";
    private const string ResumePath = "/api/v1/ops/diagnostics/resume-rollover";

    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public RolloverControlsTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the preview reads. Idempotent, so a second run is a no-op.</summary>
    public async Task InitializeAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<SeedWorld>()
            .ExecuteAsync(new SeedWorldRequest("api-integration-world"), CancellationToken.None);
    }

    /// <summary>Nothing to tear down; the collection's fixture owns the database.</summary>
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_controls_are_unreachable_when_the_diagnostics_flag_is_off()
    {
        await using var factory = _fixture.CreateFactory(enableJobProbe: false);
        using var client = factory.CreateClient();

        (await client.PostAsJsonAsync(PreviewPath, new { seasonId = (Guid?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "an unreachable feature must not leak (§17.12)");
        (await client.PostAsJsonAsync(RunPath, new { seasonId = (Guid?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync(ResumePath, new { seasonId = Guid.CreateVersion7(), reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_preview_reads_the_current_season_and_reports_it_is_not_ready()
    {
        await using var factory = _fixture.CreateFactory(enableJobProbe: false, enableRolloverTrigger: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(PreviewPath, new { seasonId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();

        preview.GetProperty("seasonId").GetGuid().Should().NotBeEmpty();
        preview.GetProperty("seasonStatus").GetString().Should().NotBeNullOrWhiteSpace();
        preview.GetProperty("preflight").GetProperty("unpublishedMatchdays").GetInt32()
            .Should()
            .BeGreaterThan(0, "a freshly seeded season has not been played (PR-4)");
        preview.GetProperty("ready").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Running_enqueues_the_real_rollover_job_and_a_resume_needs_a_reason()
    {
        await using var factory = _fixture.CreateFactory(enableJobProbe: false, enableRolloverTrigger: true);
        using var client = factory.CreateClient();

        var seasonId = await CurrentSeasonIdAsync(client);

        var run = await client.PostAsJsonAsync(RunPath, new { seasonId });

        run.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await run.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("seasonId").GetGuid().Should().Be(seasonId);
        body.GetProperty("businessKey").GetString().Should().Be($"season:{seasonId:D}:rollover");

        // A resume without the reason the audit trail requires is refused before the use case runs.
        var blank = await client.PostAsJsonAsync(ResumePath, new { seasonId, reason = "" });

        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await blank.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("code").GetString().Should().Be(CompetitionErrorCodes.RolloverResumeReasonRequired);
    }

    private static async Task<Guid> CurrentSeasonIdAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(PreviewPath, new { seasonId = (Guid?)null });
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();

        return preview.GetProperty("seasonId").GetGuid();
    }
}

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The Stage 15 stepped-clock controls over HTTP: a non-production diagnostics surface, mapped only when its
/// flag is on and the clock is stepped (`Diagnostics:EnableGameClockControl`, master plan §17.12, ADR-0049).
/// </summary>
/// <remarks>
/// The worker is not started here and must not be: the assertion is that the controls read the stored instant
/// and enqueue the worker's real advance job, and that the worker, not the API, would set the clock. Because no
/// worker runs, the stored instant never moves, which also makes the repeat-idempotency assertion exact.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class GameClockControlsTests
{
    private const string StatusPath = "/api/v1/ops/diagnostics/game-clock";
    private const string AdvancePath = "/api/v1/ops/diagnostics/advance-game-clock";

    /// <summary>The instant every stepped host in this class starts from.</summary>
    private static readonly DateTimeOffset InitialNow = new(2026, 10, 6, 18, 25, 0, TimeSpan.Zero);

    /// <summary>The start of the game day after <see cref="InitialNow"/>.</summary>
    private static readonly DateTimeOffset NextMidnight = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public GameClockControlsTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_controls_are_unreachable_when_the_flag_is_off()
    {
        await using var factory = _fixture.CreateFactory(enableJobProbe: false);
        using var client = factory.CreateClient();

        (await client.GetAsync(StatusPath)).StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "an unreachable feature must not leak (§17.12)");
        (await client.PostAsJsonAsync(AdvancePath, new { target = "day" })).StatusCode.Should().Be(
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_controls_are_unreachable_on_a_real_time_host_even_with_the_flag_on()
    {
        // The endpoints have no clock to move on a real-time or compressed host, so they are not mapped at
        // all — the flag alone is not enough (ADR-0049).
        await using var factory = _fixture.CreateFactory(
            enableJobProbe: false,
            enableGameClockControl: true);
        using var client = factory.CreateClient();

        (await client.GetAsync(StatusPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_step_reads_the_stored_instant_and_targets_the_next_day_idempotently()
    {
        await using var factory = SteppedFactory();
        using var client = factory.CreateClient();

        var status = await client.GetAsync(StatusPath);

        status.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await status.Content.ReadFromJsonAsync<JsonElement>();

        ReadInstant(read, "gameNow").Should().Be(InitialNow);
        read.TryGetProperty("nextMatchdayAt", out _).Should().BeTrue();

        var first = await client.PostAsJsonAsync(AdvancePath, new { target = "day" });

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await first.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("target").GetString().Should().Be("day");
        ReadInstant(body, "targetInstantUtc").Should().Be(NextMidnight);
        body.GetProperty("businessKey").GetString().Should().StartWith("clock:day:");

        // The clock has not moved (no worker runs), so the same request resolves the same target and the
        // queue refuses the duplicate.
        var repeated = await client.PostAsJsonAsync(AdvancePath, new { target = "day" });
        var repeatedBody = await repeated.Content.ReadFromJsonAsync<JsonElement>();

        repeatedBody.GetProperty("enqueued").GetBoolean().Should().BeFalse();

        // A matchday step with no round upcoming falls back to a day rather than doing nothing.
        var matchday = await client.PostAsJsonAsync(AdvancePath, new { target = "matchday" });

        matchday.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var matchdayBody = await matchday.Content.ReadFromJsonAsync<JsonElement>();

        matchdayBody.GetProperty("target").GetString().Should().Be("matchday");
        ReadInstant(matchdayBody, "targetInstantUtc").Should().BeAfter(InitialNow);
    }

    private WebApplicationFactory<Program> SteppedFactory() => _fixture.CreateFactory(
        enableJobProbe: false,
        enableGameClockControl: true,
        steppedClockNow: InitialNow);

    private static DateTimeOffset ReadInstant(JsonElement element, string property) =>
        DateTimeOffset.Parse(
            element.GetProperty(property).GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}

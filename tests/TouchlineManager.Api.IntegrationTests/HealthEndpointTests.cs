using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// Liveness and readiness must answer different questions, or an orchestrator will either restart a
/// healthy process or route traffic to one that cannot serve.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HealthEndpointTests
{
    private readonly ApiFixture _fixture;

    public HealthEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Liveness_reports_healthy_without_depending_on_the_database()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        document.RootElement.GetProperty("checks").GetArrayLength().Should().Be(
            0,
            "liveness must not fail because a dependency is down; that is what readiness is for");
    }

    [Fact]
    public async Task Readiness_reports_the_database_check()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checks = document.RootElement.GetProperty("checks");

        checks.GetArrayLength().Should().Be(1);
        checks[0].GetProperty("name").GetString().Should().Be("database");
        checks[0].GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Detailed_health_reports_every_check_with_its_duration()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("totalDurationMs").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        root.GetProperty("checks")[0].GetProperty("durationMs").GetDouble().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Health_endpoints_are_reachable_without_authentication()
    {
        // An orchestrator cannot authenticate, so health must be anonymous and must leak nothing but
        // component status.
        using var client = _fixture.Factory.CreateClient();

        foreach (var path in new[] { "/health/live", "/health/ready", "/health" })
        {
            var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} must be anonymous");
        }
    }
}

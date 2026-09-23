using System.Net;
using System.Text.Json;
using FluentAssertions;
using TouchlineManager.Contracts.Http;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// Every response must carry a correlation ID, and every error must be an RFC 9457 Problem Details
/// document that leaks nothing internal while still being diagnosable.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CorrelationAndErrorTests
{
    private readonly ApiFixture _fixture;

    public CorrelationAndErrorTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Every_response_carries_a_correlation_id()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.Headers.TryGetValues(ApiHeaders.CorrelationId, out var values).Should().BeTrue();
        values!.Single().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_safe_client_supplied_correlation_id_is_reused()
    {
        using var client = _fixture.Factory.CreateClient();
        const string Supplied = "support-ticket-42";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(ApiHeaders.CorrelationId, Supplied);

        var response = await client.SendAsync(request);

        response.Headers.GetValues(ApiHeaders.CorrelationId).Single().Should().Be(Supplied);
    }

    [Theory]
    [InlineData("has spaces and symbols!")]
    [InlineData("line-break\ninjected")]
    [InlineData("../../etc/passwd")]
    public async Task An_unsafe_correlation_id_is_replaced(string supplied)
    {
        // The header lands in logs, so an untrusted value must not be able to forge or split entries.
        using var client = _fixture.Factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(ApiHeaders.CorrelationId, supplied);

        var response = await client.SendAsync(request);
        var echoed = response.Headers.GetValues(ApiHeaders.CorrelationId).Single();

        echoed.Should().NotBe(supplied);
        Guid.TryParse(echoed, out _).Should().BeTrue("the server substitutes its own UUIDv7");
    }

    [Fact]
    public async Task An_over_long_correlation_id_is_replaced()
    {
        using var client = _fixture.Factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(ApiHeaders.CorrelationId, new string('a', 200));

        var response = await client.SendAsync(request);
        var echoed = response.Headers.GetValues(ApiHeaders.CorrelationId).Single();

        Guid.TryParse(echoed, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_routes_return_rfc_9457_problem_details()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(404);
        root.TryGetProperty("title", out _).Should().BeTrue();

        // ProblemDetails carries its extensions through [JsonExtensionData], so they are written as
        // sibling properties rather than nested under an "extensions" object.
        root.TryGetProperty("correlationId", out var correlationId).Should().BeTrue();
        correlationId.GetString().Should().NotBeNullOrWhiteSpace();
        root.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Problem_details_never_contains_server_internals()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("Exception");
        body.Should().NotContain("StackTrace");
        body.Should().NotContain("Npgsql");
        body.Should().NotContain("Microsoft.EntityFrameworkCore");
        body.Should().NotContain("D:\\");
    }

    [Fact]
    public async Task Error_responses_are_json_and_parse_as_utf8()
    {
        // JSON is UTF-8 by definition (RFC 8259), so the media type is the contract; an explicit
        // charset parameter is not required.
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(bytes);

        document.RootElement.GetProperty("status").GetInt32().Should().Be(404);
    }
}

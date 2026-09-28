using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Market;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The market commands are throttled per manager (`INT-5`, master plan §7.7).
/// </summary>
/// <remarks>
/// A dedicated host with a deliberately tiny market limit, so the mechanism is exercised without
/// throttling the ordinary market tests. The limiter runs before the handler, so the manager need not hold
/// a club: the third listing in the window is refused whether or not the first two would have succeeded.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class MarketRateLimitTests
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public MarketRateLimitTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Listing_commands_are_rate_limited_per_manager()
    {
        await using var factory = _fixture.CreateFactory(
            enableJobProbe: false,
            marketListingPermitLimit: 2);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var body = new CreateListingRequest(Guid.CreateVersion7(), MinimumFeeMinor: 5_000_000, Seasons: 2);

        HttpResponseMessage? throttled = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await PostListingAsync(client, body);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = response;
                break;
            }

            // The first two reach the handler, which refuses them for want of a club rather than throttling.
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        throttled.Should().NotBeNull("the third listing within the window must be throttled (INT-5)");
        (await ReadProblemCodeAsync(throttled!)).Should().Be(ApiErrorCodes.TooManyRequests);
    }

    [Fact]
    public async Task Bid_commands_are_rate_limited_per_manager()
    {
        await using var factory = _fixture.CreateFactory(
            enableJobProbe: false,
            marketBidPermitLimit: 2);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var listingId = Guid.CreateVersion7();

        HttpResponseMessage? throttled = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await PostBidAsync(client, listingId, new PlaceBidRequest(5_000_000));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = response;
                break;
            }

            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        throttled.Should().NotBeNull("the third bid within the window must be throttled (INT-5)");
        (await ReadProblemCodeAsync(throttled!)).Should().Be(ApiErrorCodes.TooManyRequests);
    }

    private static Task<HttpResponseMessage> PostListingAsync(HttpClient client, CreateListingRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers/listings")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostBidAsync(
        HttpClient client,
        Guid listingId,
        PlaceBidRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/listings/{listingId}/bids")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        return client.SendAsync(request);
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

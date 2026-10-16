using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Calendar;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The club calendar over HTTP, against the real composition root.
/// </summary>
/// <remarks>
/// The calendar knows no module, so what is checked here is the seam: that the registered sources feed the
/// one endpoint, that the window is honoured, and that the refusals are the ones every club read gives.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class CalendarTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public CalendarTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world these reads walk. Idempotent, so a second run is a no-op.</summary>
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
    public async Task A_window_around_the_seasons_fixtures_lists_each_match_once_in_time_order()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var mine = await client.GetFromJsonAsync<MyFixturesResponse>("/api/v1/fixtures/mine");
        var first = mine!.Fixtures.Min(fixture => fixture.KickoffAt).AddDays(-1);
        var until = first.AddDays(40);

        var calendar = await client.GetFromJsonAsync<CalendarResponse>(
            $"/api/v1/calendar?from={Uri.EscapeDataString(first.ToString("O"))}&to={Uri.EscapeDataString(until.ToString("O"))}");

        calendar.Should().NotBeNull();
        calendar!.From.Should().Be(first);
        calendar.To.Should().Be(until);

        var matches = calendar.Events.Where(item => item.Kind == CalendarEventKinds.Match).ToList();
        var expected = mine.Fixtures.Count(fixture => fixture.KickoffAt >= first && fixture.KickoffAt < until);

        matches.Should().HaveCount(expected).And.NotBeEmpty();
        matches.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        calendar.Events.Select(item => item.At).Should().BeInAscendingOrder();
        calendar.Events.Should().OnlyContain(item => item.At >= first && item.At < until);
        calendar.Events.Should().OnlyContain(item => item.Title.Length > 0);
        calendar.Events.Should().OnlyContain(item =>
            item.State == CalendarEventStates.Done || item.State == CalendarEventStates.Upcoming);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task With_no_bounds_the_default_window_is_served_and_says_what_today_is()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var calendar = await client.GetFromJsonAsync<CalendarResponse>("/api/v1/calendar");

        calendar.Should().NotBeNull();
        calendar!.ServerTime.Should().BeOnOrAfter(calendar.From).And.BeBefore(calendar.To);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Theory]
    [InlineData("from=2026-10-10T00:00:00Z")]
    [InlineData("from=2026-10-10T00:00:00Z&to=2026-10-09T00:00:00Z")]
    [InlineData("from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z")]
    public async Task A_malformed_reversed_or_overlong_window_is_rejected(string query)
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var response = await client.GetAsync($"/api/v1/calendar?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(CalendarErrorCodes.InvalidRange);
    }

    [Fact]
    public async Task A_manager_with_no_club_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var response = await client.GetAsync("/api/v1/calendar");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.NoClub);
    }

    [Fact]
    public async Task The_calendar_requires_a_signed_in_manager()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/calendar");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Creates the manager's profile and claims the first club the world offers.</summary>
    private static async Task OnboardAsync(HttpClient client)
    {
        var profile = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var clubId = await FirstAvailableClubAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        var claim = await client.SendAsync(request);

        claim.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>Finds the first club in the world that still has no manager.</summary>
    private static async Task<Guid> FirstAvailableClubAsync(HttpClient client)
    {
        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        foreach (var country in countries!)
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs");

            var available = listing!.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is not null)
            {
                return available.Id;
            }
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

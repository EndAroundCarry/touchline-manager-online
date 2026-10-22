using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Forum;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Forum;

namespace Tactical11Manager.Api.IntegrationTests;

/// <summary>
/// The community forum over HTTP, against the real composition root and a real database (`COM-17` to `COM-20`).
/// </summary>
/// <remarks>
/// Each test signs up its own manager and gives them a club, then resigns so the shared world does not run out of
/// free clubs. Threads written by one test stay in the shared database, so a test looks for its own thread by id
/// rather than counting.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ForumTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public ForumTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world this collection reads. Idempotent, so a second run is a no-op.</summary>
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
    public async Task The_overview_lists_the_international_subforums_with_the_english_rule_and_the_country_forum()
    {
        using var client = CreateClient();
        await SignUpAsync(client);
        await OnboardAsync(client);

        var overview = (await client.GetFromJsonAsync<ForumOverviewResponse>("/api/v1/forum/overview"))!;

        overview.Sections.Select(section => section.Id).Should().Equal(
            [ForumConstants.SectionInternational, ForumConstants.SectionCountry],
            "a manager with a club sees both forums (COM-17)");

        var international = overview.Sections[0];

        international.LanguageNotice.Should().Be(ForumConstants.InternationalLanguageNotice, "COM-19");
        international.Subforums.Select(subforum => subforum.Name).Should().Equal(
            ["General", "Development and Bugs", "Announcements"]);
        international.Subforums.Single(subforum => subforum.Name == "Announcements").CanCreateThreads.Should().BeFalse();
        international.Subforums.Where(subforum => subforum.Name != "Announcements")
            .Should().OnlyContain(subforum => subforum.CanCreateThreads);

        var country = overview.Sections[1];

        country.LanguageNotice.Should().BeNull("a country forum has no language rule");
        country.CountryId.Should().NotBeNull();
        country.Subforums.Should().ContainSingle().Which.Name.Should().Be("General");

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_new_world_has_a_pinned_welcome_announcement_that_a_manager_can_answer()
    {
        using var client = CreateClient();
        await SignUpAsync(client);
        await CreateProfileAsync(client);

        var list = (await client.GetFromJsonAsync<ForumThreadListResponse>(
            "/api/v1/forum/subforums/international-announcements/threads"))!;

        list.CanCreateThreads.Should().BeFalse();
        list.LanguageNotice.Should().Be(ForumConstants.InternationalLanguageNotice);

        var welcome = list.Threads.Should().Contain(thread => thread.IsPinned && thread.IsAnnouncement).Subject;

        // Pinned threads come first (COM-18).
        list.Threads[0].IsPinned.Should().BeTrue();

        // Announcements cannot be started by a manager, but any thread can be answered.
        (await ReplyAsync(client, welcome.Id, "Thanks, glad to be here.")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_manager_starts_a_thread_reads_it_and_a_reply_joins_it()
    {
        using var client = CreateClient();
        var manager = await SignUpAsync(client);
        await OnboardAsync(client);

        var created = await CreateThreadOkAsync(client, "international-general", "Best 4-3-3 setup?", "Wingers or inside forwards?");

        var list = (await client.GetFromJsonAsync<ForumThreadListResponse>(
            "/api/v1/forum/subforums/international-general/threads"))!;

        var summary = list.Threads.Should().Contain(thread => thread.Id == created).Subject;

        summary.Title.Should().Be("Best 4-3-3 setup?");
        summary.AuthorManagerName.Should().Be(manager.DisplayName);
        summary.AuthorClubName.Should().NotBeNullOrEmpty("the author's club is kept on the thread");
        summary.ReplyCount.Should().Be(0);

        var reply = await ReplyAsync(client, created, "Inside forwards, I think.");

        reply.StatusCode.Should().Be(HttpStatusCode.Created, "COM-18");

        var detail = (await client.GetFromJsonAsync<ForumThreadDetailResponse>($"/api/v1/forum/threads/{created}"))!;

        detail.LanguageNotice.Should().Be(ForumConstants.InternationalLanguageNotice);
        detail.SubforumName.Should().Be("General");
        detail.CanReply.Should().BeTrue();
        detail.Posts.Select(post => post.PostNumber).Should().Equal([1, 2], "posts are oldest first");
        detail.Posts.Select(post => post.Body).Should().Equal(["Wingers or inside forwards?", "Inside forwards, I think."]);
        detail.Posts.Should().OnlyContain(post => !post.IsStaff);

        var after = (await client.GetFromJsonAsync<ForumThreadListResponse>(
            "/api/v1/forum/subforums/international-general/threads"))!;

        after.Threads.Single(thread => thread.Id == created).ReplyCount.Should().Be(1);
        after.Threads.Single(thread => thread.Id == created).LatestReplyAuthorName.Should().Be(manager.DisplayName);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_manager_cannot_start_a_thread_in_announcements()
    {
        using var client = CreateClient();
        await SignUpAsync(client);
        await OnboardAsync(client);

        var response = await CreateThreadAsync(client, "international-announcements", "Big news", "Free beer for all");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "COM-18");
        (await CodeAsync(response)).Should().Be(ForumErrorCodes.AnnouncementsRestricted);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_country_thread_is_started_and_listed_in_the_managers_own_country_forum()
    {
        using var client = CreateClient();
        await SignUpAsync(client);
        await OnboardAsync(client);

        var created = await CreateThreadOkAsync(client, "country-general", "Derby day", "Who is going?");

        var list = (await client.GetFromJsonAsync<ForumThreadListResponse>(
            "/api/v1/forum/subforums/country-general/threads"))!;

        list.SectionId.Should().Be(ForumConstants.SectionCountry);
        list.LanguageNotice.Should().BeNull();
        list.Threads.Should().Contain(thread => thread.Id == created);

        // A country thread is not in the international forum.
        var general = (await client.GetFromJsonAsync<ForumThreadListResponse>(
            "/api/v1/forum/subforums/international-general/threads"))!;

        general.Threads.Should().NotContain(thread => thread.Id == created);

        var detail = (await client.GetFromJsonAsync<ForumThreadDetailResponse>($"/api/v1/forum/threads/{created}"))!;

        detail.LanguageNotice.Should().BeNull();
        detail.SectionId.Should().Be(ForumConstants.SectionCountry);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_manager_without_a_club_sees_only_the_international_forum()
    {
        using var client = CreateClient();
        await SignUpAsync(client);
        await CreateProfileAsync(client);

        var overview = (await client.GetFromJsonAsync<ForumOverviewResponse>("/api/v1/forum/overview"))!;

        overview.Sections.Should().ContainSingle().Which.Id.Should().Be(ForumConstants.SectionInternational);
        (await client.GetAsync("/api/v1/forum/subforums/country-general/threads")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var refused = await CreateThreadAsync(client, "country-general", "Hello there", "Anybody home?");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(refused)).Should().Be(ForumErrorCodes.CountryMismatch);

        // The international forum is open to them.
        (await CreateThreadAsync(client, "international-general", "Hello there", "Anybody home?"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_post_is_stored_as_written_and_the_limits_are_enforced()
    {
        using var client = CreateClient();
        await SignUpAsync(client);
        await OnboardAsync(client);

        var created = await CreateThreadOkAsync(client, "international-development-bugs", "Markup <i>test</i>", "<script>alert(1)</script>\nsecond line");
        var detail = (await client.GetFromJsonAsync<ForumThreadDetailResponse>($"/api/v1/forum/threads/{created}"))!;

        detail.Title.Should().Be("Markup <i>test</i>", "text is stored as written and drawn as text (COM-20)");
        detail.Posts.Single().Body.Should().Be("<script>alert(1)</script>\nsecond line");

        var shortTitle = await CreateThreadAsync(client, "international-general", "Hi", "A fine body");

        shortTitle.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(shortTitle)).Should().Be(ForumErrorCodes.TitleLength);

        var longBody = await CreateThreadAsync(client, "international-general", "A fine title", new string('x', ForumConstants.BodyMaxChars + 1));

        longBody.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(longBody)).Should().Be(ForumErrorCodes.BodyLength);

        var emptyReply = await ReplyAsync(client, created, "   ");

        emptyReply.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(emptyReply)).Should().Be(ForumErrorCodes.BodyLength);

        await ResignAsync(client);
    }

    [Fact]
    public async Task An_unknown_subforum_or_thread_is_not_found()
    {
        using var client = CreateClient();
        await SignUpAsync(client);
        await OnboardAsync(client);

        var subforum = await client.GetAsync("/api/v1/forum/subforums/no-such-forum/threads");

        subforum.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(subforum)).Should().Be(ForumErrorCodes.SubforumNotFound);

        (await CreateThreadAsync(client, "no-such-forum", "A fine title", "A fine body"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var thread = await client.GetAsync($"/api/v1/forum/threads/{Guid.CreateVersion7()}");

        thread.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(thread)).Should().Be(ForumErrorCodes.ThreadNotFound);
        (await ReplyAsync(client, Guid.CreateVersion7(), "Hello")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await ResignAsync(client);
    }

    [Fact]
    public async Task Posting_is_limited_per_manager_over_threads_and_replies_together()
    {
        await using var factory = _fixture.CreateFactory(enableJobProbe: false, forumPostPermitLimit: 2);

        using var alfClient = CreateClient(factory);
        using var beaClient = CreateClient(factory);

        await SignUpAsync(alfClient);
        await SignUpAsync(beaClient);
        await OnboardAsync(alfClient);
        await OnboardAsync(beaClient);

        var created = await CreateThreadOkAsync(alfClient, "international-general", "Limit test", "One");

        (await ReplyAsync(alfClient, created, "Two")).StatusCode.Should().Be(HttpStatusCode.Created);

        var throttled = await ReplyAsync(alfClient, created, "Three");

        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "COM-20");
        (await CodeAsync(throttled)).Should().Be(ApiErrorCodes.TooManyRequests);

        // Reading is not limited, and the allowance is the poster's own.
        (await alfClient.GetAsync($"/api/v1/forum/threads/{created}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReplyAsync(beaClient, created, "Mine")).StatusCode.Should().Be(HttpStatusCode.Created);

        await ResignAsync(alfClient, beaClient);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_every_forum_route()
    {
        using var client = CreateClient();
        var id = Guid.CreateVersion7();

        (await client.GetAsync("/api/v1/forum/overview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/forum/subforums/international-general/threads")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/api/v1/forum/threads/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CreateThreadAsync(client, "international-general", "A fine title", "A fine body")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReplyAsync(client, id, "Hello")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateClient() => CreateClient(_fixture.Factory);

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    /// <summary>Registers, verifies and signs in a manager, and attaches their token to the client.</summary>
    private async Task<RegisteredManager> SignUpAsync(HttpClient client)
    {
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        return manager;
    }

    private static async Task CreateProfileAsync(HttpClient client)
    {
        var profile = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    /// <summary>Creates the manager's profile and claims the first club the world offers.</summary>
    private static async Task<Guid> OnboardAsync(HttpClient client)
    {
        await CreateProfileAsync(client);

        var clubId = await FirstAvailableClubAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Created);

        return clubId;
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

    private static async Task ResignAsync(params HttpClient[] clients)
    {
        foreach (var client in clients)
        {
            await client.PostAsync("/api/v1/club-tenure/resign", content: null);
        }
    }

    private static Task<HttpResponseMessage> CreateThreadAsync(HttpClient client, string subforumId, string title, string body) =>
        client.PostAsJsonAsync($"/api/v1/forum/subforums/{subforumId}/threads", new CreateForumThreadRequest(title, body));

    private static Task<HttpResponseMessage> ReplyAsync(HttpClient client, Guid threadId, string body) =>
        client.PostAsJsonAsync($"/api/v1/forum/threads/{threadId}/posts", new CreateForumPostRequest(body));

    /// <summary>Starts a thread and returns its identity, failing the test if it was refused.</summary>
    private static async Task<Guid> CreateThreadOkAsync(HttpClient client, string subforumId, string title, string body)
    {
        var response = await CreateThreadAsync(client, subforumId, title, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await response.Content.ReadFromJsonAsync<CreatedThread>())!;

        response.Headers.Location!.ToString().Should().EndWith($"/forum/threads/{created.ThreadId}");

        return created.ThreadId;
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record CreatedThread(Guid ThreadId);
}

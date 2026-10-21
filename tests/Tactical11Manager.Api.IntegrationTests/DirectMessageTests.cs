using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Comms;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;

namespace Tactical11Manager.Api.IntegrationTests;

/// <summary>
/// Direct messages between managers over HTTP, against the real composition root (`COM-10` to `COM-16`).
/// </summary>
/// <remarks>
/// Each test signs up its own managers and gives each a club, then resigns them at the end so the shared
/// world does not run out of free clubs. A club left free after that is run by the game, which is exactly the
/// "AI manager" case the club page and the refusal need.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class DirectMessageTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public DirectMessageTests(ApiFixture fixture) => _fixture = fixture;

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
    public async Task A_message_is_sent_seen_in_the_sync_poll_read_and_answered()
    {
        using var alfClient = CreateClient();
        using var beaClient = CreateClient();
        var alf = await SignUpAsync(alfClient);
        var bea = await SignUpAsync(beaClient);
        var alfClub = await OnboardAsync(alfClient);
        var beaClub = await OnboardAsync(beaClient);

        // The club page names the manager who runs it, and carries no manager identity (COM-11).
        var page = await alfClient.GetFromJsonAsync<ClubOverviewResponse>($"/api/v1/clubs/{beaClub}");

        page!.ManagerName.Should().Be(bea.DisplayName, "the club page shows the manager's display name");
        page.IsOwnClub.Should().BeFalse();

        var own = await alfClient.GetFromJsonAsync<ClubOverviewResponse>($"/api/v1/clubs/{alfClub}");

        own!.ManagerName.Should().Be(alf.DisplayName);

        var gameMessagesBefore = (await SyncAsync(beaClient)).UnreadInboxCount;

        var start = await StartAsync(alfClient, beaClub, "Friendly this week?", "We have a free Saturday.");

        start.StatusCode.Should().Be(HttpStatusCode.Created, "COM-11");

        var sent = (await start.Content.ReadFromJsonAsync<SentDirectMessageResponse>())!;

        start.Headers.Location!.ToString().Should().EndWith($"/conversations/{sent.ConversationId}");

        // Bea hears of it through the poll she already makes, and only she does (COM-16).
        (await SyncAsync(beaClient)).UnreadDirectMessageCount.Should().Be(1);
        (await SyncAsync(alfClient)).UnreadDirectMessageCount.Should().Be(0, "a sender has nothing unread");
        (await SyncAsync(beaClient)).UnreadInboxCount.Should().Be(gameMessagesBefore, "direct messages are kept apart from the game's own");

        var list = (await beaClient.GetFromJsonAsync<ConversationListResponse>("/api/v1/conversations"))!;

        list.UnreadCount.Should().Be(1);

        var line = list.Conversations.Should().ContainSingle().Subject;

        line.Subject.Should().Be("Friendly this week?");
        line.UnreadCount.Should().Be(1);
        line.LastMessagePreview.Should().Be("We have a free Saturday.");
        line.OtherManager.DisplayName.Should().Be(alf.DisplayName);
        line.OtherManager.ClubId.Should().Be(alfClub, "the other side's club is where they are today");

        // Reading the thread changes nothing; marking it read does (COM-16).
        var thread = (await beaClient.GetFromJsonAsync<ConversationResponse>(
            $"/api/v1/conversations/{sent.ConversationId}"))!;

        thread.Messages.Should().ContainSingle().Which.FromMe.Should().BeFalse();
        thread.CanReply.Should().BeTrue();
        (await SyncAsync(beaClient)).UnreadDirectMessageCount.Should().Be(1, "a read alone does not clear the count");

        (await beaClient.PostAsync($"/api/v1/conversations/{sent.ConversationId}/read", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SyncAsync(beaClient)).UnreadDirectMessageCount.Should().Be(0, "opening the thread marks it read (COM-16)");

        var reply = await ReplyAsync(beaClient, sent.ConversationId, "Saturday works.");

        reply.StatusCode.Should().Be(HttpStatusCode.Created, "COM-12");
        (await SyncAsync(alfClient)).UnreadDirectMessageCount.Should().Be(1);

        var alfThread = (await alfClient.GetFromJsonAsync<ConversationResponse>(
            $"/api/v1/conversations/{sent.ConversationId}"))!;

        alfThread.Messages.Select(message => message.Body).Should().Equal(
            ["Saturday works.", "We have a free Saturday."],
            "a page is newest first");
        alfThread.Messages[0].FromMe.Should().BeFalse();
        alfThread.Messages[1].FromMe.Should().BeTrue();
        alfThread.Messages[1].ReadAt.Should().NotBeNull("Bea read Alf's message");

        await ResignAsync(alfClient, beaClient);
    }

    [Fact]
    public async Task Several_threads_between_two_managers_list_newest_first()
    {
        using var alfClient = CreateClient();
        using var beaClient = CreateClient();
        await SignUpAsync(alfClient);
        await SignUpAsync(beaClient);
        await OnboardAsync(alfClient);
        var beaClub = await OnboardAsync(beaClient);

        var first = await StartOkAsync(alfClient, beaClub, "First", "One");
        var second = await StartOkAsync(alfClient, beaClub, "Second", "Two");

        var list = (await alfClient.GetFromJsonAsync<ConversationListResponse>("/api/v1/conversations"))!;

        list.Conversations.Select(conversation => conversation.Id).Should().Equal(
            [second.ConversationId, first.ConversationId],
            "the most recently active thread is first (COM-10)");

        await ReplyAsync(beaClient, first.ConversationId, "Back to the first");

        var after = (await alfClient.GetFromJsonAsync<ConversationListResponse>("/api/v1/conversations"))!;

        after.Conversations.Select(conversation => conversation.Id).Should().Equal(
            [first.ConversationId, second.ConversationId],
            "a reply moves its thread to the top");

        await ResignAsync(alfClient, beaClient);
    }

    [Fact]
    public async Task A_club_run_by_the_game_shows_no_manager_and_cannot_be_written_to()
    {
        using var alfClient = CreateClient();
        await SignUpAsync(alfClient);
        var alfClub = await OnboardAsync(alfClient);
        var aiClub = await FirstAvailableClubAsync(alfClient);

        var page = await alfClient.GetFromJsonAsync<ClubOverviewResponse>($"/api/v1/clubs/{aiClub}");

        page!.ManagerName.Should().BeNull("the game runs it, so the page says \"AI manager\" (COM-11)");

        var toAi = await StartAsync(alfClient, aiClub, "Hello", "Anyone there?");

        toAi.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(toAi)).Should().Be(CommsErrorCodes.ClubHasNoManager);

        var toSelf = await StartAsync(alfClient, alfClub, "Hello", "Me?");

        toSelf.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(toSelf)).Should().Be(CommsErrorCodes.CannotMessageOwnClub);

        var toNowhere = await StartAsync(alfClient, Guid.CreateVersion7(), "Hello", "Anyone?");

        toNowhere.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(toNowhere)).Should().Be(WorldErrorCodes.ClubNotFound);

        await ResignAsync(alfClient);
    }

    [Fact]
    public async Task A_manager_with_no_club_or_no_profile_cannot_start_a_conversation()
    {
        using var alfClient = CreateClient();
        using var beaClient = CreateClient();
        using var noProfileClient = CreateClient();
        await SignUpAsync(alfClient);
        await SignUpAsync(beaClient);
        await SignUpAsync(noProfileClient);
        var alfClub = await OnboardAsync(alfClient);

        var profile = await beaClient.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var jobless = await StartAsync(beaClient, alfClub, "Hello", "I have no club");

        jobless.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(jobless)).Should().Be(CommsErrorCodes.SenderHasNoClub, "COM-11");

        var noProfile = await StartAsync(noProfileClient, alfClub, "Hello", "I have no profile");

        noProfile.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(noProfile)).Should().Be(WorldErrorCodes.ManagerProfileRequired);

        await ResignAsync(alfClient);
    }

    [Theory]
    [InlineData("", "Hello")]
    [InlineData("Hi", "   ")]
    public async Task A_message_with_no_text_is_refused(string subject, string body)
    {
        using var alfClient = CreateClient();
        using var beaClient = CreateClient();
        await SignUpAsync(alfClient);
        await SignUpAsync(beaClient);
        await OnboardAsync(alfClient);
        var beaClub = await OnboardAsync(beaClient);

        var response = await StartAsync(alfClient, beaClub, subject, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(CommsErrorCodes.InvalidMessageText, "COM-13");

        await ResignAsync(alfClient, beaClient);
    }

    [Fact]
    public async Task A_message_is_stored_and_returned_as_plain_text()
    {
        using var alfClient = CreateClient();
        using var beaClient = CreateClient();
        await SignUpAsync(alfClient);
        await SignUpAsync(beaClient);
        await OnboardAsync(alfClient);
        var beaClub = await OnboardAsync(beaClient);

        const string markup = "<script>alert(1)</script> & <b>bold</b>";
        var sent = await StartOkAsync(alfClient, beaClub, "Markup", $"{markup}\u0007\r\nsecond line");

        var thread = (await beaClient.GetFromJsonAsync<ConversationResponse>(
            $"/api/v1/conversations/{sent.ConversationId}"))!;

        thread.Messages.Single().Body.Should().Be(
            $"{markup}\nsecond line",
            "text is stored as written, minus control characters, and the client shows it as text (COM-13)");

        await ResignAsync(alfClient, beaClient);
    }

    [Fact]
    public async Task A_third_manager_cannot_see_reply_to_or_read_a_conversation()
    {
        using var alfClient = CreateClient();
        using var beaClient = CreateClient();
        using var cyClient = CreateClient();
        await SignUpAsync(alfClient);
        await SignUpAsync(beaClient);
        await SignUpAsync(cyClient);
        await OnboardAsync(alfClient);
        var beaClub = await OnboardAsync(beaClient);
        await OnboardAsync(cyClient);

        var sent = await StartOkAsync(alfClient, beaClub, "Private", "Between us");
        var url = $"/api/v1/conversations/{sent.ConversationId}";

        var read = await cyClient.GetAsync(url);
        var reply = await ReplyAsync(cyClient, sent.ConversationId, "Hello both");
        var mark = await cyClient.PostAsync($"{url}/read", content: null);

        foreach (var response in new[] { read, reply, mark })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a conversation you are not part of does not exist (I-5)");
            (await CodeAsync(response)).Should().Be(CommsErrorCodes.ConversationNotFound);
        }

        (await cyClient.GetFromJsonAsync<ConversationListResponse>("/api/v1/conversations"))!
            .Conversations.Should().BeEmpty();
        (await SyncAsync(cyClient)).UnreadDirectMessageCount.Should().Be(0);

        await ResignAsync(alfClient, beaClient, cyClient);
    }

    [Fact]
    public async Task A_block_stops_both_sides_writing_and_lifting_it_restores_sending()
    {
        using var alfClient = CreateClient();
        using var beaClient = CreateClient();
        await SignUpAsync(alfClient);
        await SignUpAsync(beaClient);
        await OnboardAsync(alfClient);
        var beaClub = await OnboardAsync(beaClient);

        var sent = await StartOkAsync(alfClient, beaClub, "Hello", "Hi Bea");
        var beaView = (await beaClient.GetFromJsonAsync<ConversationResponse>(
            $"/api/v1/conversations/{sent.ConversationId}"))!;
        var alfId = beaView.OtherManager.ManagerId;

        (await beaClient.PutAsync($"/api/v1/blocked-managers/{alfId}", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await beaClient.PutAsync($"/api/v1/blocked-managers/{alfId}", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "blocking twice is harmless");

        var blockedReply = await ReplyAsync(alfClient, sent.ConversationId, "Please?");

        blockedReply.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(blockedReply)).Should().Be(CommsErrorCodes.ManagerBlocked, "COM-15");

        var blockerReply = await ReplyAsync(beaClient, sent.ConversationId, "No");

        blockerReply.StatusCode.Should().Be(HttpStatusCode.Conflict, "a block stops the blocker too (COM-15)");

        var newStart = await StartAsync(alfClient, beaClub, "Again", "Hello again");

        newStart.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(newStart)).Should().Be(CommsErrorCodes.ManagerBlocked);

        // Existing messages stay readable, and each side is told why a reply is closed (COM-15).
        var alfView = (await alfClient.GetFromJsonAsync<ConversationResponse>(
            $"/api/v1/conversations/{sent.ConversationId}"))!;

        alfView.Messages.Should().ContainSingle();
        alfView.CanReply.Should().BeFalse();
        alfView.ReplyBlockedReason.Should().Be("This manager is not accepting your messages.");

        var beaBlocked = (await beaClient.GetFromJsonAsync<ConversationResponse>(
            $"/api/v1/conversations/{sent.ConversationId}"))!;

        beaBlocked.BlockedByMe.Should().BeTrue();
        beaBlocked.CanReply.Should().BeFalse();

        (await beaClient.DeleteAsync($"/api/v1/blocked-managers/{alfId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReplyAsync(alfClient, sent.ConversationId, "Please?")).StatusCode
            .Should().Be(HttpStatusCode.Created, "after unblocking, sends work (COM-15)");
        (await ReplyAsync(beaClient, sent.ConversationId, "Fine")).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var unknown = await beaClient.PutAsync($"/api/v1/blocked-managers/{Guid.CreateVersion7()}", content: null);

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(unknown)).Should().Be(CommsErrorCodes.ManagerNotFound);

        await ResignAsync(alfClient, beaClient);
    }

    [Fact]
    public async Task Sending_is_limited_per_manager_over_starts_and_replies_together()
    {
        await using var factory = _fixture.CreateFactory(enableJobProbe: false, directMessagePermitLimit: 3);

        using var alfClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
        using var beaClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

        await SignUpAsync(alfClient);
        await SignUpAsync(beaClient);
        await OnboardAsync(alfClient);
        var beaClub = await OnboardAsync(beaClient);

        var sent = await StartOkAsync(alfClient, beaClub, "Limit", "One");

        (await ReplyAsync(alfClient, sent.ConversationId, "Two")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReplyAsync(alfClient, sent.ConversationId, "Three")).StatusCode.Should().Be(HttpStatusCode.Created);

        var throttled = await ReplyAsync(alfClient, sent.ConversationId, "Four");

        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "COM-14");
        (await CodeAsync(throttled)).Should().Be(ApiErrorCodes.TooManyRequests);

        // The allowance is the sender's own: the other manager can still write.
        (await ReplyAsync(beaClient, sent.ConversationId, "Reply")).StatusCode.Should().Be(HttpStatusCode.Created);

        await ResignAsync(alfClient, beaClient);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_every_direct_message_route()
    {
        using var client = CreateClient();
        var id = Guid.CreateVersion7();

        (await client.GetAsync("/api/v1/conversations")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/api/v1/conversations/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StartAsync(client, id, "Hi", "Hello")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReplyAsync(client, id, "Hello")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"/api/v1/conversations/{id}/read", content: null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsync($"/api/v1/blocked-managers/{id}", content: null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync($"/api/v1/blocked-managers/{id}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cursor_this_server_did_not_produce_is_refused_by_name()
    {
        using var alfClient = CreateClient();
        await SignUpAsync(alfClient);
        await OnboardAsync(alfClient);

        var response = await alfClient.GetAsync("/api/v1/conversations?cursor=not-a-cursor");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(CommsErrorCodes.InvalidCursor);

        await ResignAsync(alfClient);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
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

    /// <summary>Creates the manager's profile and claims the first club the world offers.</summary>
    private static async Task<Guid> OnboardAsync(HttpClient client)
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

    private static Task<HttpResponseMessage> StartAsync(HttpClient client, Guid clubId, string subject, string body) =>
        client.PostAsJsonAsync("/api/v1/conversations", new StartConversationRequest(clubId, subject, body));

    private static Task<HttpResponseMessage> ReplyAsync(HttpClient client, Guid conversationId, string body) =>
        client.PostAsJsonAsync(
            $"/api/v1/conversations/{conversationId}/messages",
            new SendDirectMessageRequest(body));

    /// <summary>Starts a conversation and returns where it went, failing the test if it was refused.</summary>
    private static async Task<SentDirectMessageResponse> StartOkAsync(
        HttpClient client,
        Guid clubId,
        string subject,
        string body)
    {
        var response = await StartAsync(client, clubId, subject, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<SentDirectMessageResponse>())!;
    }

    private static async Task<SyncResponse> SyncAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<SyncResponse>("/api/v1/sync"))!;

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

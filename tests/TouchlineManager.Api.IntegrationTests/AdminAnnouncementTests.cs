using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Comms;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The operator's announcement over HTTP: a scoped notice published to the news feed (master plan §10.8, §13,
/// `F-46`, `F-47`, ADR-0045).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdminAnnouncementTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AdminAnnouncementTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the announcement is published into. Idempotent with the other admin tests.</summary>
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
    public async Task Publishing_an_announcement_is_not_reachable_anonymously()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await ActionAsync(client, "Hello", "A notice.", code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_plain_manager_cannot_publish_an_announcement()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var response = await ActionAsync(client, "Hello", "A notice.", "000000");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Publishing_an_announcement_without_a_fresh_code_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(client, "Hello", "A notice.", code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.MfaCodeInvalid);
    }

    [Fact]
    public async Task Publishing_an_announcement_without_an_idempotency_key_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/announcements")
        {
            Content = JsonContent.Create(new { title = "Hello", body = "A notice.", reason = "no key" }),
        };
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, MfaScenario.Code(op.Secret));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task An_operator_publishes_an_announcement_and_the_reason_is_audited()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        const string title = "Scheduled maintenance";
        const string body = "The game will be read-only tonight from 22:00 to 22:30 UTC.";
        const string reason = "maintenance window announced";

        var response = await ActionAsync(client, title, body, MfaScenario.Code(op.Secret), reason);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var published = (await response.Content.ReadFromJsonAsync<AdminAnnouncementResponse>())!;

        published.Category.Should().Be("announcement");
        published.NewsItemId.Should().NotBeEmpty();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var item = await db.NewsItems.AsNoTracking().SingleAsync(row => row.Id == published.NewsItemId);

        item.Category.Should().Be(NewsCategory.Announcement);
        item.TemplateKey.Should().Be(NewsTemplates.AnnouncementPublished);
        item.CountryId.Should().BeNull();
        item.DivisionId.Should().BeNull();

        var text = NewsMessageText.Render(item.TemplateKey, item.ParametersJson);

        text.Title.Should().Be(title);
        text.Body.Should().Be(body);

        (await db.AuditEntries.AsNoTracking().Where(row => row.TargetId == item.Id).ToListAsync())
            .Should()
            .Contain(row => row.Action == AdminAuditActions.AnnouncementPublished && row.Reason == reason);
    }

    [Fact]
    public async Task An_announcement_with_no_title_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(client, title: "   ", body: "A notice.", MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.AnnouncementInvalid);
    }

    [Fact]
    public async Task An_announcement_scoped_to_an_unknown_country_is_not_found()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/announcements")
        {
            Content = JsonContent.Create(new
            {
                title = "Hello",
                body = "A notice.",
                countryId = Guid.CreateVersion7(),
                reason = "scoped to nowhere",
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, MfaScenario.Code(op.Secret));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.AnnouncementScopeNotFound);
    }

    private static Task<HttpResponseMessage> ActionAsync(
        HttpClient client,
        string title,
        string body,
        string? code,
        string reason = "a notice to every manager")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/announcements")
        {
            Content = JsonContent.Create(new { title, body, reason }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        if (code is not null)
        {
            request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, code);
        }

        return client.SendAsync(request);
    }
}

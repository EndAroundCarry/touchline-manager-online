using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Status;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The public service status read (master plan §16 Stage 15, `F-55`, ADR-0050).
/// </summary>
/// <remarks>
/// <para>
/// This is the product's first anonymous read, so its being reachable without a session is the point rather
/// than an accident: the register consent and the footer link to the pages it feeds while signed out.
/// </para>
/// <para>
/// The disclosure boundary is asserted as executable policy — the body carries public game data, the
/// operator's maintenance reason, and the document versions, and no address can appear in it
/// (<c>docs/security/data-classification.md</c> §2, `LGL-1`).
/// </para>
/// </remarks>
[Collection(PublicStatusCollection.Name)]
public sealed class PublicStatusTests : IAsyncLifetime
{
    private const string Path = "/api/v1/status";

    private const string Reason = "Read-only while we repair the ledger.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public PublicStatusTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the status is read against, and clears the incident flag.</summary>
    public async Task InitializeAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<SeedWorld>()
            .ExecuteAsync(new SeedWorldRequest("api-integration-world"), CancellationToken.None);

        await ClearReadOnlyAsync(scope);
    }

    /// <summary>Clears the flag, so the read-only case cannot leak into the next test or another suite.</summary>
    public async Task DisposeAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        await ClearReadOnlyAsync(scope);
    }

    [Fact]
    public async Task The_status_is_readable_without_a_session_and_carries_no_personal_data()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync(Path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue("it is live state, so it is never cached");

        var payload = await response.Content.ReadAsStringAsync();

        payload.Should().NotContain("@", "the public status must carry no account address");

        var status = JsonSerializer.Deserialize<PublicStatusResponse>(payload, JsonOptions)!;

        status.ReadOnly.Should().BeFalse();
        status.ReadOnlyMessage.Should().BeNull();
        status.SeasonNumber.Should().BeGreaterThan(0, "the world is seeded");
        status.NextMatchdayAt.Should().NotBeNull("a seeded season has rounds ahead of it");
        status.Documents.TermsVersion.Should().NotBeNullOrWhiteSpace();
        status.Documents.PrivacyVersion.Should().NotBeNullOrWhiteSpace();
        status.ServerTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task The_status_carries_the_operators_reason_while_the_game_is_read_only()
    {
        using var operatorClient = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, operatorClient);
        operatorClient.WithBearer(op.AccessToken);

        var enabled = await SetReadOnlyAsync(operatorClient, MfaScenario.Code(op.Secret), enabled: true);
        enabled.StatusCode.Should().Be(HttpStatusCode.Created);

        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync(Path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = (await response.Content.ReadFromJsonAsync<PublicStatusResponse>())!;

        status.ReadOnly.Should().BeTrue();
        status.ReadOnlyMessage.Should().Be(Reason);
    }

    private static Task<HttpResponseMessage> SetReadOnlyAsync(HttpClient client, string code, bool enabled)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/feature-flags/{IncidentFlags.ReadOnly}")
        {
            Content = JsonContent.Create(new { value = new { enabled, message = Reason }, reason = Reason }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, code);

        return client.SendAsync(request);
    }

    private static async Task ClearReadOnlyAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var readOnly = scope.ServiceProvider.GetRequiredService<IReadOnlyMode>();

        var existing = await db.FeatureFlags.SingleOrDefaultAsync(
            flag => flag.Scope == IncidentFlags.WorldScope && flag.Key == IncidentFlags.ReadOnly);

        if (existing is not null)
        {
            db.FeatureFlags.Remove(existing);
            await db.SaveChangesAsync();
        }

        // The host caches the flag for a few seconds; drop it so the next test starts from the database.
        readOnly.Invalidate();
    }
}

/// <summary>
/// Shares one API host and one database for the public status tests, isolated from the shared <c>api</c>
/// collection.
/// </summary>
/// <remarks>
/// Its own instance of <see cref="ApiFixture"/> — and so its own container — deliberately. The <c>api</c>
/// collection seeds a single mutable world, and a class added to it shifts the order its clubs are claimed
/// in, which some of its order-sensitive tests depend on. A host per purpose keeps this suite from perturbing
/// them, the same argument <see cref="MatchApiFixture"/> makes for the match reads.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PublicStatusCollection : ICollectionFixture<ApiFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "api-status";
}

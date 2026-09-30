using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Ops;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The operator read console: who may read the job queue, a matchday, and the audit trail, and what each
/// read returns (master plan §10.8, §13, `F-46`, `F-47`, ADR-0043).
/// </summary>
/// <remarks>
/// The gate is asserted from every side: anonymous, a plain manager, and an operator with a completed
/// second factor; and a support operator, which the reads admit but the mutations do not.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AdminConsoleTests : IAsyncLifetime
{
    private const string JobsPath = "/api/v1/admin/jobs";
    private const string AuditPath = "/api/v1/admin/audit";

    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AdminConsoleTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the console reads report on. Idempotent with the other admin tests.</summary>
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
    public async Task The_operator_console_is_not_readable_anonymously()
    {
        using var client = _fixture.Factory.CreateClient();
        var matchdayId = await APendingMatchdayIdAsync();

        foreach (var path in new[] { JobsPath, AuditPath, $"/api/v1/admin/matchdays/{matchdayId}" })
        {
            var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} is behind a role", path);
        }
    }

    [Fact]
    public async Task A_plain_manager_is_forbidden_from_the_console()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var matchdayId = await APendingMatchdayIdAsync();

        foreach (var path in new[] { JobsPath, AuditPath, $"/api/v1/admin/matchdays/{matchdayId}" })
        {
            var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "{0} needs a role and a factor", path);
        }
    }

    [Fact]
    public async Task An_operator_with_a_second_factor_reads_the_console()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var matchdayId = await APendingMatchdayIdAsync();

        var jobs = await client.GetAsync(JobsPath);
        jobs.StatusCode.Should().Be(HttpStatusCode.OK);
        jobs.Headers.CacheControl!.NoStore.Should().BeTrue("it is live operator data");

        var audit = await client.GetAsync(AuditPath);
        audit.StatusCode.Should().Be(HttpStatusCode.OK);
        audit.Headers.CacheControl!.NoStore.Should().BeTrue();

        var matchday = await client.GetAsync($"/api/v1/admin/matchdays/{matchdayId}");
        matchday.StatusCode.Should().Be(HttpStatusCode.OK);
        matchday.Headers.CacheControl!.NoStore.Should().BeTrue();

        var detail = (await matchday.Content.ReadFromJsonAsync<AdminMatchdayDetailResponse>())!;

        detail.RoundNumber.Should().BeGreaterThan(0);
        detail.PublicationStatus.Should().Be(MatchdayPublicationStatuses.PendingCode);
        detail.LockAt.Should().BeBefore(detail.KickoffAt);
        detail.DivisionName.Should().NotBeNullOrWhiteSpace();
        detail.CountryCode.Should().NotBeNullOrWhiteSpace();
        detail.Fixtures.Should().HaveCount(9, "a round is nine fixtures (MAT-7)");
        detail.Fixtures.Should().OnlyContain(fixture => !string.IsNullOrWhiteSpace(fixture.HomeClubName));
    }

    [Fact]
    public async Task A_support_operator_may_read_the_console()
    {
        using var client = _fixture.Factory.CreateClient();
        var support = await MfaScenario.CreateWithRoleAsync(_fixture, client, UserRoles.Support);
        client.WithBearer(support.AccessToken);

        var response = await client.GetAsync(JobsPath);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "support may read; it is excluded only from mutation (E-3)");
    }

    [Fact]
    public async Task The_job_queue_filters_by_status_and_never_serializes_the_payload()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var jobType = await InsertJobsAsync(count: 1, status: JobStatuses.DeadLetteredCode);

        var response = await client.GetAsync($"{JobsPath}?status=dead_letter&jobType={jobType}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();

        using (var document = JsonDocument.Parse(body))
        {
            var items = document.RootElement.GetProperty("items");

            items.GetArrayLength().Should().Be(1);
            items[0].GetProperty("status").GetString().Should().Be(JobStatuses.DeadLetteredCode);
            items[0].GetProperty("businessKey").GetString().Should().NotBeNullOrWhiteSpace();
            items[0].TryGetProperty("payload", out _).Should().BeFalse("the handler payload is internal JSON");
        }

        var page = (await response.Content.ReadFromJsonAsync<AdminJobPageResponse>())!;

        page.NextCursor.Should().BeNull("the filter selects fewer than a page");
    }

    [Fact]
    public async Task The_job_queue_pages_by_keyset()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var jobType = await InsertJobsAsync(count: 30, status: JobStatuses.PendingCode);

        var first = (await client.GetFromJsonAsync<AdminJobPageResponse>($"{JobsPath}?jobType={jobType}"))!;

        first.Items.Should().HaveCount(25);
        first.NextCursor.Should().NotBeNullOrWhiteSpace();

        var second = (await client.GetFromJsonAsync<AdminJobPageResponse>(
            $"{JobsPath}?jobType={jobType}&cursor={Uri.EscapeDataString(first.NextCursor!)}"))!;

        second.Items.Should().HaveCount(5);
        second.NextCursor.Should().BeNull("the last page has no successor");

        first.Items.Select(item => item.Id)
            .Concat(second.Items.Select(item => item.Id))
            .Should().OnlyHaveUniqueItems().And.HaveCount(30, "the keyset walk neither skips nor repeats");
    }

    [Fact]
    public async Task The_console_refuses_a_bad_filter_and_a_bad_cursor_by_name()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var badStatus = await client.GetAsync($"{JobsPath}?status=exploded");
        badStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(badStatus)).Should().Be(AdminErrorCodes.InvalidFilter);

        var badJobCursor = await client.GetAsync($"{JobsPath}?cursor=not-a-cursor");
        badJobCursor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(badJobCursor)).Should().Be(AdminErrorCodes.InvalidCursor);

        var badAuditCursor = await client.GetAsync($"{AuditPath}?cursor=not-a-cursor");
        badAuditCursor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(badAuditCursor)).Should().Be(AdminErrorCodes.InvalidCursor);
    }

    [Fact]
    public async Task An_unknown_matchday_is_not_found()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await client.GetAsync($"/api/v1/admin/matchdays/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_audit_search_returns_a_recorded_suspension_without_its_ip_hash()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);

        using var targetClient = _fixture.Factory.CreateClient();
        var target = await AuthScenario.CreateVerifiedManagerAsync(_fixture, targetClient);

        client.WithBearer(op.AccessToken);

        var reason = $"console test {Guid.NewGuid():N}";

        var suspension = await SuspendAsync(client, op, target.UserId, reason);
        suspension.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.GetAsync(
            $"{AuditPath}?action=admin.account.suspended&targetId={target.UserId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();

        using (var document = JsonDocument.Parse(body))
        {
            var items = document.RootElement.GetProperty("items");

            items.GetArrayLength().Should().BeGreaterThan(0);
            items[0].TryGetProperty("ipHash", out _).Should().BeFalse("the hashed client IP is not an operator read");
        }

        var page = (await response.Content.ReadFromJsonAsync<AdminAuditPageResponse>())!;

        page.Items.Should().Contain(entry => entry.Action == AdminAuditActions.AccountSuspended
            && entry.Reason == reason
            && entry.TargetId == target.UserId);
    }

    private async Task<Guid> APendingMatchdayIdAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await db.Matchdays
            .Where(matchday => matchday.PublicationStatus == MatchdayPublicationStatus.Pending)
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstAsync();
    }

    private async Task<string> InsertJobsAsync(int count, string status)
    {
        var jobType = $"ops.console-test.{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        for (var index = 0; index < count; index++)
        {
            db.Jobs.Add(new OpsJob
            {
                Id = Guid.CreateVersion7(),
                JobType = jobType,
                BusinessKey = $"{jobType}:{index}",
                Payload = "{}",
                DueAt = now,
                Priority = 0,
                Status = status,
                AttemptCount = 0,
                MaxAttempts = 8,
                CreatedAt = now.AddSeconds(index),
                UpdatedAt = now.AddSeconds(index),
                Version = 1,
            });
        }

        await db.SaveChangesAsync();

        return jobType;
    }

    private static Task<HttpResponseMessage> SuspendAsync(
        HttpClient client,
        OperatorSession op,
        Guid userId,
        string reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{userId}/suspend")
        {
            Content = JsonContent.Create(new { reason }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, MfaScenario.Code(op.Secret));

        return client.SendAsync(request);
    }
}

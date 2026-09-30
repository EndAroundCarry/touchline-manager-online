using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Jobs;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Ops;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The operator recovery commands: retry and cancel a job, and resume a stuck round (master plan §10.8, §13,
/// `F-46`, `F-47`, ADR-0044).
/// </summary>
/// <remarks>
/// The gate is asserted from every side — anonymous, a plain manager, an operator without a fresh code, and
/// one without an idempotency key — and each command is exercised through the real queue and the real
/// audit trail.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AdminRecoveryTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AdminRecoveryTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the recovery commands act on. Idempotent with the other admin tests.</summary>
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
    public async Task The_recovery_commands_are_not_reachable_anonymously()
    {
        using var client = _fixture.Factory.CreateClient();

        foreach (var path in RecoveryPaths())
        {
            var response = await ActionAsync(client, path, "anonymous", code: null);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} is behind a role", path);
        }
    }

    [Fact]
    public async Task A_plain_manager_cannot_use_the_recovery_commands()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        foreach (var path in RecoveryPaths())
        {
            var response = await ActionAsync(client, path, "not mine", code: "000000");

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "{0} needs a role", path);
        }
    }

    [Fact]
    public async Task A_recovery_command_without_a_fresh_code_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(client, $"/api/v1/admin/jobs/{Guid.CreateVersion7()}/retry", "no code", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.MfaCodeInvalid);
    }

    [Fact]
    public async Task A_recovery_command_without_an_idempotency_key_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/jobs/{Guid.CreateVersion7()}/retry")
        {
            Content = JsonContent.Create(new { reason = "no key" }),
        };
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, MfaScenario.Code(op.Secret));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task An_operator_retries_a_dead_lettered_job_and_the_reason_is_audited()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var jobId = await InsertJobAsync(JobStatuses.DeadLetteredCode);
        var reason = $"recovery test {Guid.NewGuid():N}";

        var response = await ActionAsync(client, $"/api/v1/admin/jobs/{jobId}/retry", reason, MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = (await response.Content.ReadFromJsonAsync<AdminJobActionResponse>())!;
        body.JobId.Should().Be(jobId);
        body.Status.Should().Be(JobStatuses.PendingCode);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var row = await db.Jobs.AsNoTracking().SingleAsync(job => job.Id == jobId);

        row.Status.Should().Be(JobStatuses.PendingCode);
        row.AttemptCount.Should().Be(0, "the retried job gets a clean budget");
        row.LastError.Should().BeNull();

        (await db.AuditEntries.AsNoTracking().Where(entry => entry.TargetId == jobId).ToListAsync())
            .Should()
            .Contain(entry => entry.Action == AdminAuditActions.JobRetried && entry.Reason == reason);
    }

    [Fact]
    public async Task Retrying_a_job_that_is_not_dead_lettered_is_a_conflict()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var jobId = await InsertJobAsync(JobStatuses.PendingCode);

        var response = await ActionAsync(
            client,
            $"/api/v1/admin/jobs/{jobId}/retry",
            "not a dead letter",
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.JobNotRetryable);
    }

    [Fact]
    public async Task Retrying_an_unknown_job_is_not_found()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(
            client,
            $"/api/v1/admin/jobs/{Guid.CreateVersion7()}/retry",
            "no such job",
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.JobNotFound);
    }

    [Fact]
    public async Task An_operator_cancels_a_stuck_job()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var jobId = await InsertJobAsync(JobStatuses.PendingCode);

        var response = await ActionAsync(
            client,
            $"/api/v1/admin/jobs/{jobId}/cancel",
            "the round was voided by hand",
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = (await response.Content.ReadFromJsonAsync<AdminJobActionResponse>())!;
        body.Status.Should().Be(JobStatuses.CancelledCode);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        (await db.Jobs.AsNoTracking().SingleAsync(job => job.Id == jobId)).Status
            .Should().Be(JobStatuses.CancelledCode);

        (await db.AuditEntries.AsNoTracking().Where(entry => entry.TargetId == jobId).ToListAsync())
            .Should()
            .Contain(entry => entry.Action == AdminAuditActions.JobCancelled);
    }

    [Fact]
    public async Task Cancelling_a_completed_job_is_a_conflict()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var jobId = await InsertJobAsync(JobStatuses.CompletedCode);

        var response = await ActionAsync(
            client,
            $"/api/v1/admin/jobs/{jobId}/cancel",
            "already done",
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.JobNotCancellable);
    }

    [Fact]
    public async Task An_operator_resumes_a_stuck_matchday()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var matchdayId = await APendingMatchdayWithoutResolveJobAsync();
        var reason = $"recovery test {Guid.NewGuid():N}";

        var response = await ActionAsync(
            client,
            $"/api/v1/admin/matchdays/{matchdayId}/resume",
            reason,
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = (await response.Content.ReadFromJsonAsync<AdminMatchdayResumeResponse>())!;
        body.MatchdayId.Should().Be(matchdayId);
        body.Step.Should().Be("resolve");

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var resolveKey = MatchdayJobTypes.ResolveKey(matchdayId);

        (await db.Jobs.AsNoTracking().SingleAsync(job => job.BusinessKey == resolveKey)).Status
            .Should().Be(JobStatuses.PendingCode, "the round's resolution is back on the queue");

        (await db.AuditEntries.AsNoTracking().Where(entry => entry.TargetId == matchdayId).ToListAsync())
            .Should()
            .Contain(entry => entry.Action == AdminAuditActions.MatchdayResumed && entry.Reason == reason);
    }

    [Fact]
    public async Task Resuming_a_matchday_whose_job_is_already_queued_is_a_conflict()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        // A healthy round: its resolution job is pending, so there is nothing to resume.
        var matchdayId = await APendingMatchdayAsync();
        await InsertMatchdayJobAsync(MatchdayJobTypes.Resolve, MatchdayJobTypes.ResolveKey(matchdayId));

        var response = await ActionAsync(
            client,
            $"/api/v1/admin/matchdays/{matchdayId}/resume",
            "nothing is stuck",
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.MatchdayNotResumable);
    }

    [Fact]
    public async Task Resuming_an_unknown_matchday_is_not_found()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(
            client,
            $"/api/v1/admin/matchdays/{Guid.CreateVersion7()}/resume",
            "no such round",
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.MatchdayNotFound);
    }

    private static IEnumerable<string> RecoveryPaths() =>
    [
        $"/api/v1/admin/jobs/{Guid.CreateVersion7()}/retry",
        $"/api/v1/admin/jobs/{Guid.CreateVersion7()}/cancel",
        $"/api/v1/admin/matchdays/{Guid.CreateVersion7()}/resume",
    ];

    private static Task<HttpResponseMessage> ActionAsync(
        HttpClient client,
        string path,
        string reason,
        string? code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { reason }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        if (code is not null)
        {
            request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, code);
        }

        return client.SendAsync(request);
    }

    private async Task<Guid> APendingMatchdayAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await db.Matchdays
            .Where(matchday => matchday.PublicationStatus == MatchdayPublicationStatus.Pending)
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstAsync();
    }

    private async Task<Guid> APendingMatchdayWithoutResolveJobAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var matchdays = await db.Matchdays
            .Where(matchday => matchday.PublicationStatus == MatchdayPublicationStatus.Pending)
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .Take(50)
            .ToListAsync();

        foreach (var matchdayId in matchdays)
        {
            var resolveKey = MatchdayJobTypes.ResolveKey(matchdayId);

            if (!await db.Jobs.AnyAsync(job => job.BusinessKey == resolveKey))
            {
                return matchdayId;
            }
        }

        throw new InvalidOperationException("Every pending matchday already has a resolution job.");
    }

    private async Task<Guid> InsertJobAsync(string status)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.CreateVersion7();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        db.Jobs.Add(new OpsJob
        {
            Id = id,
            JobType = $"ops.recovery-test.{Guid.NewGuid():N}",
            BusinessKey = $"ops.recovery-test:{Guid.NewGuid():N}",
            Payload = "{}",
            DueAt = now,
            Status = status,
            AttemptCount = 0,
            MaxAttempts = 8,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });

        await db.SaveChangesAsync();

        return id;
    }

    private async Task InsertMatchdayJobAsync(string jobType, string businessKey)
    {
        var now = DateTimeOffset.UtcNow;

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        db.Jobs.Add(new OpsJob
        {
            Id = Guid.CreateVersion7(),
            JobType = jobType,
            BusinessKey = businessKey,
            Payload = "{}",
            DueAt = now,
            Status = JobStatuses.PendingCode,
            AttemptCount = 0,
            MaxAttempts = 8,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        });

        await db.SaveChangesAsync();
    }
}

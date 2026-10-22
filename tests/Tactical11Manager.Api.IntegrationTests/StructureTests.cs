using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Comms;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Api.IntegrationTests;

/// <summary>
/// The snack bar and the merchandise shop over HTTP, against the real composition root (`STRC-1`…`STRC-9`).
/// </summary>
/// <remarks>
/// The tests share one world, and a club's buildings outlive the manager who ordered the work, so none of them
/// assumes level 1: each reads the buildings it was given and asserts what its own order changed. Each puts
/// the club's buildings back to level 1 when it is done, because other suites price the club on the levels it
/// opens with.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class StructureTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public StructureTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world these tests walk. Idempotent, so a second run is a no-op.</summary>
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
    public async Task A_manager_reads_both_buildings_with_their_levels_costs_and_bonus_tables()
    {
        using var client = CreateClient();
        var clubId = await SignInAndOnboardAsync(client);

        var response = await client.GetAsync("/api/v1/structures");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var structures = (await response.Content.ReadFromJsonAsync<StructuresResponse>())!;

        structures.AvailableCashMinor.Should().BePositive();
        structures.SnackBar.Kind.Should().Be("snack_bar");
        structures.Merchandise.Kind.Should().Be("merchandise");

        foreach (var building in new[] { structures.SnackBar, structures.Merchandise })
        {
            building.MaxLevel.Should().Be(10);
            building.Level.Should().BeInRange(1, 10);
            building.Version.Should().BePositive();
            building.UpkeepWeeklyMinor.Should().BePositive();
            building.Building.Should().BeNull("nothing has been ordered");
            building.SeasonTakingsMinor.Should().BeGreaterThanOrEqualTo(0);
            building.SeasonUpkeepMinor.Should().BeGreaterThanOrEqualTo(0);
        }

        structures.SnackBar.SpendMinor.Should().Be(StructureRuleSet.SnackSpendMinorFor(structures.SnackBar.Level, 1));
        structures.Merchandise.SpendMinor.Should().Be(StructureRuleSet.MerchSpendMinorFor(structures.Merchandise.Level, 1));

        var next = structures.SnackBar.Next;

        next.Should().NotBeNull("a building below level 10 can be upgraded");
        next!.Level.Should().Be(structures.SnackBar.Level + 1);
        next.CostMinor.Should().BePositive();
        next.BuildDays.Should().BeGreaterThanOrEqualTo(1);
        next.SpendMinor.Should().BeGreaterThan(structures.SnackBar.SpendMinor, "a better building earns more per customer");
        next.UpkeepWeeklyMinor.Should().BeGreaterThan(structures.SnackBar.UpkeepWeeklyMinor);

        structures.SnackBar.Bonuses.Select(bonus => (bonus.Reason, bonus.Bp)).Should().Equal(
            ("underdog_win", 3_000),
            ("even_win", 1_500),
            ("favourite_win", 500),
            ("underdog_draw", 1_000));
        structures.SnackBar.Bonuses.Max(bonus => bonus.Bp).Should().Be(StructureRuleSet.MaxResultBonusBp);

        structures.Merchandise.Bonuses.Select(bonus => bonus.Bp).Should().Equal(0, 0, 200, 500, 1_000, 1_800, 2_500);
        structures.Merchandise.Bonuses.Should().ContainSingle(bonus => bonus.Current, "the fans are in exactly one mood");
        structures.Merchandise.RecentMatches.Should().BeEmpty("the shop sells by the week, not by the match");

        await ResetAsync(clubId);
        await ResignAsync(client);
    }

    [Fact]
    public async Task An_upgrade_is_paid_for_in_full_trades_at_the_old_level_and_rises_when_the_job_runs()
    {
        using var client = CreateClient();
        var clubId = await SignInAndOnboardAsync(client);

        try
        {
            var before = await ReadAsync(client);
            var snack = before.SnackBar;
            var next = snack.Next!;

            next.CostMinor.Should().BeLessThanOrEqualTo(before.AvailableCashMinor, "the arrangement: the club can pay");

            var response = await UpgradeAsync(client, "snack_bar", snack.Version);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var after = (await response.Content.ReadFromJsonAsync<StructuresResponse>())!;

            after.SnackBar.Level.Should().Be(snack.Level, "the building trades at its old level until the work is done (STRC-3)");
            after.SnackBar.Version.Should().BeGreaterThan(snack.Version);
            after.SnackBar.Building.Should().NotBeNull();
            after.SnackBar.Building!.TargetLevel.Should().Be(next.Level);

            var wait = after.SnackBar.Building.DueAt - after.ServerTime;

            wait.Should().BeCloseTo(TimeSpan.FromDays(next.BuildDays), TimeSpan.FromMinutes(1));
            after.AvailableCashMinor.Should().Be(before.AvailableCashMinor - next.CostMinor, "paid in full when ordered");
            after.Merchandise.Version.Should().Be(before.Merchandise.Version, "the other building is untouched");
            response.Headers.ETag!.Tag.Should().Be($"\"{after.SnackBar.Version}\"");

            var ledger = (await client.GetFromJsonAsync<FinanceLedgerResponse>("/api/v1/finances/ledger"))!;

            ledger.Entries[0].Category.Should().Be("structure_upgrade");
            ledger.Entries[0].CashDeltaMinor.Should().Be(-next.CostMinor);

            var structureId = await StructureIdAsync(clubId, StructureKind.SnackBar);
            var jobKey = StructureJobTypes.CompleteUpgradeKey(structureId, next.Level);

            await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
                var job = await db.Jobs.SingleAsync(candidate => candidate.BusinessKey == jobKey);

                job.JobType.Should().Be(StructureJobTypes.CompleteUpgrade);
                job.Status.Should().Be("pending");
                job.DueAt.Should().BeCloseTo(after.SnackBar.Building.DueAt, TimeSpan.FromMilliseconds(5));
            }

            // The work is still going: a second order for the same building is refused by name, whether it holds
            // the old version (a retry) or the new one (a second click that read the fresh page).
            var retried = await UpgradeAsync(client, "snack_bar", snack.Version);

            retried.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            (await CodeAsync(retried)).Should().Be(ApiErrorCodes.PreconditionFailed);

            var second = await UpgradeAsync(client, "snack_bar", after.SnackBar.Version);

            second.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeAsync(second)).Should().Be(StructureErrorCodes.Building);

            (await ReadAsync(client)).AvailableCashMinor.Should().Be(after.AvailableCashMinor, "only the first order was paid");

            // The job runs, twice, as the queue's at-least-once delivery allows.
            await RunCompletionAsync(structureId, next.Level);
            await RunCompletionAsync(structureId, next.Level);

            var done = await ReadAsync(client);

            done.SnackBar.Level.Should().Be(next.Level);
            done.SnackBar.Building.Should().BeNull();
            done.SnackBar.SpendMinor.Should().Be(next.SpendMinor);
            done.SnackBar.UpkeepWeeklyMinor.Should().Be(next.UpkeepWeeklyMinor);

            var inbox = (await client.GetFromJsonAsync<InboxResponse>("/api/v1/inbox"))!;
            var messages = inbox.Messages.Where(message => message.TemplateKey == StructureInboxTemplates.UpgradeCompleted).ToList();

            messages.Should().ContainSingle("a second delivery of the job tells the manager nothing more");
            messages[0].Category.Should().Be("system");
            messages[0].RelatedEntityId.Should().Be(structureId);
            messages[0].Title.Should().Be($"Your snack bar is now level {next.Level}");
        }
        finally
        {
            await ResetAsync(clubId);
            await ResignAsync(client);
        }
    }

    [Fact]
    public async Task An_order_without_a_version_is_refused()
    {
        using var client = CreateClient();
        var clubId = await SignInAndOnboardAsync(client);

        var response = await UpgradeAsync(client, "merchandise", version: null);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await CodeAsync(response)).Should().Be(ApiErrorCodes.PreconditionRequired);

        await ResetAsync(clubId);
        await ResignAsync(client);
    }

    [Fact]
    public async Task An_order_for_a_building_that_does_not_exist_is_refused_by_name()
    {
        using var client = CreateClient();
        var clubId = await SignInAndOnboardAsync(client);

        var response = await UpgradeAsync(client, "gift_shop", version: 1);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(StructureErrorCodes.UnknownKind);

        await ResetAsync(clubId);
        await ResignAsync(client);
    }

    [Fact]
    public async Task A_building_at_the_top_level_cannot_be_upgraded_and_offers_no_next_level()
    {
        using var client = CreateClient();
        var clubId = await SignInAndOnboardAsync(client);

        try
        {
            await SetLevelAsync(clubId, StructureKind.Merchandise, StructureRuleSet.MaxLevel);

            var structures = await ReadAsync(client);

            structures.Merchandise.Level.Should().Be(10);
            structures.Merchandise.Next.Should().BeNull();

            var response = await UpgradeAsync(client, "merchandise", structures.Merchandise.Version);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeAsync(response)).Should().Be(StructureErrorCodes.MaxLevel);
        }
        finally
        {
            await ResetAsync(clubId);
            await ResignAsync(client);
        }
    }

    [Fact]
    public async Task An_upgrade_the_club_cannot_pay_for_is_refused_and_starts_nothing()
    {
        using var client = CreateClient();
        var clubId = await SignInAndOnboardAsync(client);

        var before = await ReadAsync(client);
        var drained = before.AvailableCashMinor - 1;

        await PostLedgerAsync(clubId, drained, debit: true);

        try
        {
            var response = await UpgradeAsync(client, "snack_bar", before.SnackBar.Version);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeAsync(response)).Should().Be(StructureErrorCodes.InsufficientFunds);

            var after = await ReadAsync(client);

            after.SnackBar.Building.Should().BeNull();
            after.SnackBar.Version.Should().Be(before.SnackBar.Version);
            after.AvailableCashMinor.Should().Be(1);
        }
        finally
        {
            await PostLedgerAsync(clubId, drained, debit: false);
            await ResetAsync(clubId);
            await ResignAsync(client);
        }
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

        var read = await client.GetAsync("/api/v1/structures");
        var upgrade = await UpgradeAsync(client, "snack_bar", version: 1);

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(read)).Should().Be(SquadErrorCodes.NoClub);
        upgrade.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(upgrade)).Should().Be(SquadErrorCodes.NoClub);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Signs in a new manager, claims the first free club, and returns that club.</summary>
    private async Task<Guid> SignInAndOnboardAsync(HttpClient client)
    {
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var profile = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var clubId = await FirstAvailableClubAsync(client);
        var claim = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        claim.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        (await client.SendAsync(claim)).StatusCode.Should().Be(HttpStatusCode.Created);

        // A fresh claim can find the buildings of an earlier manager of the club mid-work; start from rest.
        await ResetAsync(clubId);

        return clubId;
    }

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

    private static async Task<StructuresResponse> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<StructuresResponse>("/api/v1/structures"))!;

    private static Task<HttpResponseMessage> UpgradeAsync(HttpClient client, string kind, long? version)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/structures/{kind}/upgrade");

        if (version is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        }

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ResignAsync(HttpClient client) =>
        client.PostAsync("/api/v1/club-tenure/resign", content: null);

    private async Task<Guid> StructureIdAsync(Guid clubId, StructureKind kind)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.ClubStructures
            .Where(building => building.ClubId == clubId && building.Kind == kind)
            .Select(building => building.Id)
            .SingleAsync();
    }

    /// <summary>Runs the job's work, as the worker would when the build time is up.</summary>
    private async Task RunCompletionAsync(Guid structureId, int targetLevel)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<CompleteStructureUpgrade>()
            .ExecuteAsync(structureId, targetLevel, CancellationToken.None);
    }

    private async Task SetLevelAsync(Guid clubId, StructureKind kind, int level)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        await db.ClubStructures
            .Where(building => building.ClubId == clubId && building.Kind == kind)
            .ExecuteUpdateAsync(setters => setters.SetProperty(building => building.Level, level));
    }

    /// <summary>Puts a club's buildings back to level 1 with no work under way, and clears the jobs for it.</summary>
    private async Task ResetAsync(Guid clubId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var ids = await db.ClubStructures
            .Where(building => building.ClubId == clubId)
            .Select(building => building.Id)
            .ToListAsync();

        await db.ClubStructures
            .Where(building => building.ClubId == clubId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(building => building.Level, 1)
                .SetProperty(building => building.UpgradeTargetLevel, (int?)null)
                .SetProperty(building => building.UpgradeDueAt, (DateTimeOffset?)null));

        foreach (var id in ids)
        {
            var prefix = $"structure:{id:D}:";

            await db.Jobs
                .Where(job => job.JobType == StructureJobTypes.CompleteUpgrade && job.BusinessKey.StartsWith(prefix))
                .ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// Moves cash out of, or back into, a club's account directly, so a test can leave it nearly empty and
    /// then put it back for the suites that share the club.
    /// </summary>
    private async Task PostLedgerAsync(Guid clubId, long amountMinor, bool debit)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);

        // A debit books as an upgrade of a building that does not exist, a credit as a snack bar's takings at
        // a fixture that does not exist: each is a distinct correlation key, so neither collides with a real one.
        var posting = debit
            ? Tactical11Manager.Application.Finance.LedgerPostings.StructureUpgrade(
                Guid.CreateVersion7(), clubId, Guid.CreateVersion7(), 2, StructureKind.SnackBar, 2, amountMinor)
            : Tactical11Manager.Application.Finance.LedgerPostings.SnackBarTakings(
                Guid.CreateVersion7(), clubId, Guid.CreateVersion7(), 1, 1, 0, amountMinor);

        db.LedgerEntries.Add(account.Post(posting, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

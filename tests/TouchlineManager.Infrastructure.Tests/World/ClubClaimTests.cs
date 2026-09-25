using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.World;

/// <summary>
/// Club takeover: the Stage 3 exit criteria for ownership (master plan §7.6, `WORLD-8`).
/// </summary>
/// <remarks>
/// <para>
/// Every claim here is made through the use case the API endpoint calls, against real PostgreSQL, with the
/// real advisory locks. That is the point: "exactly one winner" and "exactly one provisioning request" are
/// claims about concurrency, and a test that stubbed the database would be testing the stub.
/// </para>
/// <para>
/// The tests share one seeded world, so each one takes a country for itself — Spain for the claim rules,
/// Germany for filling a tier, Italy for which tier is claimable, France for occupancy, Romania for
/// resignation and account state. Clubs are always found by asking for a free one, so no test depends on
/// which other test ran first.
/// </para>
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class ClubClaimTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public ClubClaimTests(WorldFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task A_manager_takes_over_an_ai_club_and_inherits_it()
    {
        await using var scope = Fixture.CreateScope();
        var clubId = await FreeClubIdAsync(scope, CountryId("ESP"), CancellationToken.None);
        var userId = await CreateManagerAsync(scope, CancellationToken.None);

        var result = await ClaimAsync(scope, userId, clubId, NewKey());

        result.Outcome.Should().Be(ClaimClubOutcome.Claimed);

        var dashboard = result.Dashboard!;

        dashboard.Club.Id.Should().Be(clubId);
        dashboard.Control.Status.Should().Be("active");
        dashboard.Control.TenureId.Should().NotBeNull();

        // WORLD-9: the club arrives as it is, with its finances intact rather than reset.
        dashboard.Finances.CashMinor.Should().Be(WorldRuleSet.OpeningCashMinorTier1);
        dashboard.Finances.AvailableMinor.Should().Be(WorldRuleSet.OpeningCashMinorTier1);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var managerId = await ManagerIdOfAsync(db, userId);
        var tenure = await db.ClubTenures.SingleAsync(candidate => candidate.ManagerId == managerId);

        tenure.ClubId.Should().Be(clubId);
        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Active);
        tenure.EndedAt.Should().BeNull();
    }

    [Fact]
    public async Task Two_takeovers_of_one_club_produce_exactly_one_winner()
    {
        Guid clubId;
        Guid firstUserId;
        Guid secondUserId;

        await using (var scope = Fixture.CreateScope())
        {
            clubId = await FreeClubIdAsync(scope, CountryId("ESP"), CancellationToken.None);
            firstUserId = await CreateManagerAsync(scope, CancellationToken.None);
            secondUserId = await CreateManagerAsync(scope, CancellationToken.None);
        }

        var results = await Task.WhenAll(
            ClaimInOwnScopeAsync(clubId, firstUserId, NewKey()),
            ClaimInOwnScopeAsync(clubId, secondUserId, NewKey()));

        results.Count(result => result.Outcome == ClaimClubOutcome.Claimed).Should().Be(1);
        results.Count(result => result.Outcome == ClaimClubOutcome.ClubAlreadyClaimed).Should().Be(1);

        await using var verification = Fixture.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        (await db.ClubTenures.CountAsync(tenure => tenure.ClubId == clubId && tenure.EndedAt == null))
            .Should().Be(1, "one club can never hold two open tenures (OCC-9)");
    }

    [Fact]
    public async Task A_manager_cannot_hold_two_clubs()
    {
        await using var scope = Fixture.CreateScope();
        var countryId = CountryId("ESP");
        var userId = await CreateManagerAsync(scope, CancellationToken.None);

        var first = await ClaimAsync(
            scope,
            userId,
            await FreeClubIdAsync(scope, countryId, CancellationToken.None),
            NewKey());

        first.Outcome.Should().Be(ClaimClubOutcome.Claimed);

        var second = await ClaimAsync(
            scope,
            userId,
            await FreeClubIdAsync(scope, countryId, CancellationToken.None),
            NewKey());

        second.Outcome.Should().Be(ClaimClubOutcome.ManagerHasActiveClub);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var managerId = await db.Managers
            .Where(manager => manager.UserId == userId)
            .Select(manager => manager.Id)
            .SingleAsync();

        (await db.ClubTenures.CountAsync(
            tenure => tenure.ManagerId == managerId && tenure.EndedAt == null)).Should().Be(1);
    }

    [Fact]
    public async Task A_club_another_manager_holds_is_refused_while_the_country_still_has_room()
    {
        // The useful answer is "pick another club", which is only true while there is room for one.
        await using var scope = Fixture.CreateScope();
        var countryId = CountryId("ESP");
        var clubId = await FreeClubIdAsync(scope, countryId, CancellationToken.None);

        await ClaimAsync(scope, await CreateManagerAsync(scope, CancellationToken.None), clubId, NewKey());

        var second = await ClaimAsync(
            scope,
            await CreateManagerAsync(scope, CancellationToken.None),
            clubId,
            NewKey());

        second.Outcome.Should().Be(ClaimClubOutcome.ClubAlreadyClaimed);

        var capacity = await scope.ServiceProvider
            .GetRequiredService<IOnboardingQueries>()
            .GetCountryCapacityAsync(countryId, CancellationToken.None);

        capacity!.HumanOccupiedClubs.Should().BeLessThan(capacity.ClubsInLowestTier);
    }

    [Fact]
    public async Task An_idempotent_retry_returns_the_first_outcome_and_creates_no_second_tenure()
    {
        await using var scope = Fixture.CreateScope();
        var clubId = await FreeClubIdAsync(scope, CountryId("ESP"), CancellationToken.None);
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var key = NewKey();

        var first = await ClaimAsync(scope, userId, clubId, key);
        var retry = await ClaimAsync(scope, userId, clubId, key);

        first.Outcome.Should().Be(ClaimClubOutcome.Claimed);
        retry.Outcome.Should().Be(ClaimClubOutcome.Claimed);
        retry.Dashboard!.Control.TenureId.Should().Be(first.Dashboard!.Control.TenureId);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        (await db.ClubTenures.CountAsync(tenure => tenure.TakeoverIdempotencyKey == key)).Should().Be(1);
    }

    [Fact]
    public async Task Replaying_a_key_whose_claim_has_ended_is_refused_rather_than_replayed()
    {
        // The key stays globally unique, so a second tenure under it is impossible. Answering "Claimed"
        // with a club the manager has already given up would be worse than refusing and asking for a new
        // key.
        await using var scope = Fixture.CreateScope();
        var countryId = CountryId("ROU");
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var clubId = await FreeClubIdAsync(scope, countryId, CancellationToken.None);
        var key = NewKey();

        (await ClaimAsync(scope, userId, clubId, key)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        var resigned = await scope.ServiceProvider
            .GetRequiredService<ResignClub>()
            .ExecuteAsync(userId, CancellationToken.None);

        resigned.Outcome.Should().Be(ResignClubOutcome.Resigned);

        var replay = await ClaimAsync(scope, userId, clubId, key);

        replay.Outcome.Should().Be(ClaimClubOutcome.IdempotencyKeyReused);
        replay.Dashboard.Should().BeNull();
    }

    [Fact]
    public async Task An_idempotency_key_belonging_to_another_claim_is_refused()
    {
        // Answering with the first claim's club would hand this caller somebody else's outcome.
        await using var scope = Fixture.CreateScope();
        var countryId = CountryId("ESP");
        var key = NewKey();

        await ClaimAsync(
            scope,
            await CreateManagerAsync(scope, CancellationToken.None),
            await FreeClubIdAsync(scope, countryId, CancellationToken.None),
            key);

        var result = await ClaimAsync(
            scope,
            await CreateManagerAsync(scope, CancellationToken.None),
            await FreeClubIdAsync(scope, countryId, CancellationToken.None),
            key);

        result.Outcome.Should().Be(ClaimClubOutcome.IdempotencyKeyReused);
    }

    [Fact]
    public async Task Filling_a_tier_creates_exactly_one_provisioning_request_and_then_refuses_with_capacity()
    {
        // PYR-2 and PYR-10: the eighteenth manager triggers the next tier; the nineteenth is told to wait
        // rather than told to pick another club that does not exist.
        var countryId = CountryId("GER");

        await using (var scope = Fixture.CreateScope())
        {
            var clubIds = await ClubIdsOfAsync(scope, countryId, CancellationToken.None);

            clubIds.Should().HaveCount(WorldRuleSet.ClubsPerDivision);

            foreach (var clubId in clubIds)
            {
                var userId = await CreateManagerAsync(scope, CancellationToken.None);
                var result = await ClaimAsync(scope, userId, clubId, NewKey());

                result.Outcome.Should().Be(ClaimClubOutcome.Claimed);
            }
        }

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var requests = await db.DivisionProvisioningRequests
                .Where(request => request.CountryId == countryId)
                .ToListAsync();

            requests.Should().HaveCount(
                1,
                "unique (country_id, target_tier) plus the country lock decides this, not the caller's timing");
            requests[0].TargetTier.Should().Be(2);
            requests[0].Status.Should().Be(ProvisioningRequestStatus.Requested);

            var capacity = await scope.ServiceProvider
                .GetRequiredService<IOnboardingQueries>()
                .GetCountryCapacityAsync(countryId, CancellationToken.None);

            capacity!.HumanOccupiedClubs.Should().Be(capacity.ClubsInLowestTier);
            capacity.ClubsInLowestTier.Should().Be(WorldRuleSet.ClubsPerDivision);

            var anyClubId = await db.Clubs
                .Where(club => club.CountryId == countryId)
                .Select(club => club.Id)
                .FirstAsync();

            var refused = await ClaimAsync(
                scope,
                await CreateManagerAsync(scope, CancellationToken.None),
                anyClubId,
                NewKey());

            refused.Outcome.Should().Be(ClaimClubOutcome.CapacityProvisioning);
            refused.Provisioning.Should().NotBeNull();
            refused.Provisioning!.TargetTier.Should().Be(2);
            refused.Dashboard.Should().BeNull("PYR-10 forbids partially assigning a club");

            (await db.DivisionProvisioningRequests.CountAsync(request => request.CountryId == countryId))
                .Should().Be(1, "a refusal must not queue a second tier");
        }
    }

    [Fact]
    public async Task Only_the_lowest_active_tier_is_claimable()
    {
        // WORLD-8: once a lower tier is active, the tier above it stops accepting new managers, and the new
        // tier is claimable immediately.
        await using var scope = Fixture.CreateScope();
        var countryId = CountryId("ITA");
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var topTierClubId = await TopTierClubIdAsync(db, countryId, tierNumber: 1);

        var lowerDivisionId = await CreateActiveTierAsync(scope, countryId, tierNumber: 2, CancellationToken.None);

        var lowerTierClubId = await db.ClubSeasonEntries
            .Where(entry => db.DivisionSeasons
                .Where(divisionSeason => divisionSeason.DivisionId == lowerDivisionId)
                .Select(divisionSeason => divisionSeason.Id)
                .Contains(entry.DivisionSeasonId))
            .Select(entry => entry.ClubId)
            .FirstAsync();

        var upper = await ClaimAsync(
            scope,
            await CreateManagerAsync(scope, CancellationToken.None),
            topTierClubId,
            NewKey());

        upper.Outcome.Should().Be(ClaimClubOutcome.ClubNotClaimable);

        var lower = await ClaimAsync(
            scope,
            await CreateManagerAsync(scope, CancellationToken.None),
            lowerTierClubId,
            NewKey());

        lower.Outcome.Should().Be(ClaimClubOutcome.Claimed);
    }

    [Fact]
    public async Task An_inactive_tenure_still_occupies_its_club_but_a_suspended_account_does_not()
    {
        // OCC-8 and decision D-1: a manager who has gone quiet still holds their club, so a tier cannot
        // provision a new one underneath them. A suspended account is excluded, because §3.3 says so.
        await using var scope = Fixture.CreateScope();
        var countryId = CountryId("FRA");
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var clubId = await FreeClubIdAsync(scope, countryId, CancellationToken.None);

        (await ClaimAsync(scope, userId, clubId, NewKey())).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var queries = scope.ServiceProvider.GetRequiredService<IOnboardingQueries>();

        (await queries.GetCountryCapacityAsync(countryId, CancellationToken.None))!
            .HumanOccupiedClubs.Should().Be(1);

        // Scoped to this test's own manager: another test may have held and released the same club.
        var managerId = await ManagerIdOfAsync(db, userId);

        var tenure = await db.ClubTenures.SingleAsync(candidate => candidate.ManagerId == managerId);

        tenure.MarkInactive(Fixture.Clock.UtcNow);
        await db.SaveChangesAsync(CancellationToken.None);

        (await queries.GetCountryCapacityAsync(countryId, CancellationToken.None))!
            .HumanOccupiedClubs.Should().Be(1, "an inactive manager may return, so their club is still taken");

        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);

        user.Suspend("suspended-stamp", Fixture.Clock.UtcNow);
        await db.SaveChangesAsync(CancellationToken.None);

        (await queries.GetCountryCapacityAsync(countryId, CancellationToken.None))!
            .HumanOccupiedClubs.Should().Be(0, "a suspended account loses its club's place in the pyramid");
    }

    [Fact]
    public async Task Resigning_returns_the_club_starts_the_cooldown_and_keeps_the_club_intact()
    {
        // OCC-4 and OCC-5: the tenure closes, the manager waits before taking another club, and nothing
        // about the club itself changes.
        await using var scope = Fixture.CreateScope();
        var countryId = CountryId("ROU");
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var clubId = await FreeClubIdAsync(scope, countryId, CancellationToken.None);

        await ClaimAsync(scope, userId, clubId, NewKey());

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var managerId = await ManagerIdOfAsync(db, userId);

        var cashBefore = await db.ClubAccounts
            .Where(account => account.ClubId == clubId)
            .Select(account => account.CashMinor)
            .SingleAsync(CancellationToken.None);

        var resigned = await scope.ServiceProvider
            .GetRequiredService<ResignClub>()
            .ExecuteAsync(userId, CancellationToken.None);

        var expectedCooldown = Fixture.Clock.UtcNow.Add(WorldRuleSet.ResignationCooldown);

        resigned.Outcome.Should().Be(ResignClubOutcome.Resigned);
        resigned.State!.Tenure.Should().BeNull();
        resigned.State.Manager!.TakeoverCooldownUntil.Should().Be(expectedCooldown);

        // Scoped to this test's own manager: another test may have held and released the same club.
        var tenure = await db.ClubTenures.SingleAsync(candidate => candidate.ManagerId == managerId);

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Closed);
        tenure.EndReason.Should().Be(ClubTenureEndReasons.Resigned);

        // OCC-5: nothing about the club rewound.
        var cashAfter = await db.ClubAccounts
            .Where(account => account.ClubId == clubId)
            .Select(account => account.CashMinor)
            .SingleAsync(CancellationToken.None);

        cashAfter.Should().Be(cashBefore);

        // The cooldown blocks this manager rather than the club: another manager may take it immediately.
        var movedOn = await ClaimAsync(
            scope,
            userId,
            await FreeClubIdAsync(scope, countryId, CancellationToken.None),
            NewKey());

        movedOn.Outcome.Should().Be(ClaimClubOutcome.ManagerInCooldown);
        movedOn.CooldownUntil.Should().Be(expectedCooldown);

        var successor = await ClaimAsync(
            scope,
            await CreateManagerAsync(scope, CancellationToken.None),
            clubId,
            NewKey());

        successor.Outcome.Should().Be(ClaimClubOutcome.Claimed);
    }

    [Fact]
    public async Task A_suspended_account_cannot_take_a_club()
    {
        await using var scope = Fixture.CreateScope();
        var userId = await CreateUserAsync(scope, UserStatus.Suspended, CancellationToken.None);

        await CreateManagerForAsync(scope, userId, CancellationToken.None);

        var result = await ClaimAsync(
            scope,
            userId,
            await FreeClubIdAsync(scope, CountryId("ROU"), CancellationToken.None),
            NewKey());

        result.Outcome.Should().Be(ClaimClubOutcome.AccountUnavailable);
        result.Dashboard.Should().BeNull();
    }

    [Fact]
    public async Task An_account_without_a_profile_is_told_to_create_one()
    {
        await using var scope = Fixture.CreateScope();
        var userId = await CreateUserAsync(scope, UserStatus.Active, CancellationToken.None);

        var result = await ClaimAsync(
            scope,
            userId,
            await FreeClubIdAsync(scope, CountryId("ROU"), CancellationToken.None),
            NewKey());

        result.Outcome.Should().Be(ClaimClubOutcome.ManagerProfileRequired);
    }

    private Guid CountryId(string code) =>
        Fixture.Countries.Single(country => country.Code == code).Id;

    /// <summary>Resolves the manager profile an account owns.</summary>
    private static Task<Guid> ManagerIdOfAsync(TouchlineManagerDbContext db, Guid userId) =>
        db.Managers.Where(manager => manager.UserId == userId).Select(manager => manager.Id).SingleAsync();

    private static string NewKey() => Guid.CreateVersion7().ToString();

    private static Task<ClaimClubResult> ClaimAsync(
        AsyncServiceScope scope,
        Guid userId,
        Guid clubId,
        string idempotencyKey) =>
        scope.ServiceProvider
            .GetRequiredService<ClaimClub>()
            .ExecuteAsync(
                userId,
                new ClaimClubRequest { ClubId = clubId },
                idempotencyKey,
                CancellationToken.None);

    /// <summary>Runs one claim in its own scope, so that two of them can race.</summary>
    private async Task<ClaimClubResult> ClaimInOwnScopeAsync(Guid clubId, Guid userId, string idempotencyKey)
    {
        await using var scope = Fixture.CreateScope();

        return await ClaimAsync(scope, userId, clubId, idempotencyKey);
    }

    /// <summary>Finds a club in one tier of a country.</summary>
    private static Task<Guid> TopTierClubIdAsync(
        TouchlineManagerDbContext db,
        Guid countryId,
        int tierNumber) =>
        (from entry in db.ClubSeasonEntries
         join divisionSeason in db.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
         join division in db.Divisions on divisionSeason.DivisionId equals division.Id
         where division.CountryId == countryId && division.TierNumber == tierNumber
         orderby entry.ClubId
         select entry.ClubId).FirstAsync(CancellationToken.None);
}

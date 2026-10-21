using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Auth;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.World;

/// <summary>
/// The world schema's database-level guarantees, against real PostgreSQL 17 (master plan §15.2).
/// </summary>
/// <remarks>
/// These are the invariants that make onboarding safe under concurrency and that no application check
/// can provide: two partial unique indexes are what stop a club or a manager from holding two open
/// tenures when two claims race, and the check on <c>(country_id, target_tier)</c> is what makes a
/// duplicate tier impossible (`PYR-3`, `OCC-9`).
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class WorldPersistenceTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public WorldPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_club_cannot_hold_two_open_tenures()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (_, clubId) = await ArrangeClubAsync(scope);
        var firstManager = await CreateManagerAsync(scope);
        var secondManager = await CreateManagerAsync(scope);

        db.ClubTenures.Add(ClubTenure.Start(Guid.CreateVersion7(), clubId, firstManager, Key(), now));
        await db.SaveChangesAsync();

        db.ClubTenures.Add(ClubTenure.Start(Guid.CreateVersion7(), clubId, secondManager, Key(), now));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_club_tenures_open_club");
    }

    [Fact]
    public async Task A_manager_cannot_hold_two_open_tenures()
    {
        // OCC-9, and the database is where it has to hold: two concurrent claims by one manager would
        // both pass an application-level check.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (worldId, firstClubId) = await ArrangeClubAsync(scope);
        var countryId = await ReadCountryIdAsync(db, firstClubId);
        var managerId = await CreateManagerAsync(scope);
        var secondClubId = await CreateClubAsync(scope, worldId, countryId, $"Second Vale {Guid.NewGuid():N}");

        db.ClubTenures.Add(ClubTenure.Start(Guid.CreateVersion7(), firstClubId, managerId, Key(), now));
        await db.SaveChangesAsync();

        db.ClubTenures.Add(ClubTenure.Start(Guid.CreateVersion7(), secondClubId, managerId, Key(), now));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_club_tenures_open_manager");
    }

    [Fact]
    public async Task Closing_a_tenure_lets_both_the_club_and_the_manager_be_claimed_again()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (_, clubId) = await ArrangeClubAsync(scope);
        var firstManager = await CreateManagerAsync(scope);
        var secondManager = await CreateManagerAsync(scope);

        var ended = ClubTenure.Start(Guid.CreateVersion7(), clubId, firstManager, Key(), now);
        ended.Close(ClubTenureEndReasons.Resigned, now.AddDays(1));
        db.ClubTenures.Add(ended);
        await db.SaveChangesAsync();

        // The partial index only covers open tenures, so history does not block a new manager.
        db.ClubTenures.Add(ClubTenure.Start(Guid.CreateVersion7(), clubId, secondManager, Key(), now.AddDays(8)));

        await db.SaveChangesAsync();

        var open = await db.ClubTenures.CountAsync(tenure => tenure.ClubId == clubId && tenure.EndedAt == null);
        open.Should().Be(1);
    }

    [Fact]
    public async Task A_takeover_idempotency_key_can_only_be_used_once()
    {
        // This is what makes a retried takeover a no-op instead of a second tenure (CONC-3).
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (worldId, clubId) = await ArrangeClubAsync(scope);
        var countryId = await ReadCountryIdAsync(db, clubId);
        var secondClubId = await CreateClubAsync(scope, worldId, countryId, $"Shared Key Vale {Guid.NewGuid():N}");
        var key = Key();

        db.ClubTenures.Add(ClubTenure.Start(Guid.CreateVersion7(), clubId, await CreateManagerAsync(scope), key, now));
        await db.SaveChangesAsync();

        db.ClubTenures.Add(ClubTenure.Start(
            Guid.CreateVersion7(),
            secondClubId,
            await CreateManagerAsync(scope),
            key,
            now));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_club_tenures_takeover_idempotency_key");
    }

    [Fact]
    public async Task A_world_cannot_hold_two_clubs_with_the_same_normalized_name()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (worldId, clubId) = await ArrangeClubAsync(scope);
        var countryId = await ReadCountryIdAsync(db, clubId);
        var existing = await db.Clubs.SingleAsync(club => club.Id == clubId);

        db.Clubs.Add(Club.Generate(
            Guid.CreateVersion7(),
            worldId,
            countryId,
            new ClubIdentity(existing.Name.ToUpperInvariant(), "DUP", "Elsewhere", "Elsewhere", "seed-dup"),
            tier: 1,
            foundingGameYear: 2026,
            now: _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_clubs_world_id_normalized_name");
    }

    [Fact]
    public async Task Two_clubs_cannot_share_a_slug_even_when_their_names_differ()
    {
        // The slug is derived by folding and collapsing, so "Vale United" and "Vale  United" normalize
        // differently but slug identically. Without the slug index they would be two clubs with one URL.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (worldId, clubId) = await ArrangeClubAsync(scope, name: "Vale United");
        var countryId = await ReadCountryIdAsync(db, clubId);

        db.Clubs.Add(Club.Generate(
            Guid.CreateVersion7(),
            worldId,
            countryId,
            new ClubIdentity("Vale  United", "VLU", "Vale", "Vale", "seed-slug"),
            tier: 1,
            foundingGameYear: 2026,
            now: _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_clubs_world_id_slug");
    }

    [Fact]
    public async Task The_same_club_name_is_allowed_in_a_different_world()
    {
        // WORLD-1: the indexes are per world, so a test world can mirror production names.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, firstClubId) = await ArrangeClubAsync(scope, name: "Twin Vale");
        var existing = await db.Clubs.SingleAsync(club => club.Id == firstClubId);

        var otherWorldId = Guid.CreateVersion7();
        db.GameWorlds.Add(GameWorld.Create(otherWorldId, $"Second World {Guid.NewGuid():N}", _fixture.Clock.UtcNow));
        await db.SaveChangesAsync();

        var otherCountryId = Guid.CreateVersion7();
        db.Countries.Add(Country.Create(otherCountryId, otherWorldId, LaunchCountries.All[0], _fixture.Clock.UtcNow));
        await db.SaveChangesAsync();

        db.Clubs.Add(Club.Generate(
            Guid.CreateVersion7(),
            otherWorldId,
            otherCountryId,
            new ClubIdentity(existing.Name, "TWN", "Twin", "Twin", "seed-twin"),
            tier: 1,
            foundingGameYear: 2026,
            now: _fixture.Clock.UtcNow));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_country_can_only_request_one_provisioning_run_per_target_tier()
    {
        // PYR-3: this unique index, plus the country advisory lock, is what stops two concurrent
        // takeovers from creating the same tier twice.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (_, clubId) = await ArrangeClubAsync(scope);
        var countryId = await ReadCountryIdAsync(db, clubId);
        var seasonId = await db.Seasons.Select(season => season.Id).FirstAsync();

        db.DivisionProvisioningRequests.Add(
            DivisionProvisioningRequest.Request(Guid.CreateVersion7(), countryId, 2, seasonId, "seed-a", now));
        await db.SaveChangesAsync();

        db.DivisionProvisioningRequests.Add(
            DivisionProvisioningRequest.Request(Guid.CreateVersion7(), countryId, 2, seasonId, "seed-b", now));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_division_provisioning_requests_country_id_target_tier");
    }

    [Fact]
    public async Task A_provisioning_run_for_tier_one_is_refused_by_the_database()
    {
        // The aggregate refuses it too (PYR-2); the constraint is what stops a future code path from
        // inserting one with raw SQL.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, clubId) = await ArrangeClubAsync(scope);
        var countryId = await ReadCountryIdAsync(db, clubId);
        var seasonId = await db.Seasons.Select(season => season.Id).FirstAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "insert into world.division_provisioning_requests "
            + "(id, country_id, target_tier, target_season_id, status, generation_seed, requested_at, created_at, updated_at, version) "
            + "values ({0}, {1}, 1, {2}, 'requested', 'seed', now(), now(), now(), 1)",
            Guid.CreateVersion7(),
            countryId,
            seasonId);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_division_provisioning_requests_target_tier");
    }

    [Fact]
    public async Task A_division_must_hold_exactly_eighteen_clubs()
    {
        // WORLD-4: "18" is the only legal division size in the MVP, and it is enforced in the schema so
        // no generated tier can be a different shape.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, clubId) = await ArrangeClubAsync(scope);
        var countryId = await ReadCountryIdAsync(db, clubId);
        var seasonId = await db.Seasons.Select(season => season.Id).FirstAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "insert into competition.divisions "
            + "(id, country_id, tier_number, display_name, status, created_season_id, capacity, created_at, updated_at, version) "
            + "values ({0}, {1}, 9, 'Bad Division', 'provisioning', {2}, 20, now(), now(), 1)",
            Guid.CreateVersion7(),
            countryId,
            seasonId);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_divisions_capacity");
    }

    [Fact]
    public async Task A_country_cannot_have_two_divisions_at_the_same_tier()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, clubId) = await ArrangeClubAsync(scope);
        var countryId = await ReadCountryIdAsync(db, clubId);
        var seasonId = await db.Seasons.Select(season => season.Id).FirstAsync();

        db.Divisions.Add(Division.Provision(
            Guid.CreateVersion7(),
            countryId,
            4,
            "England",
            seasonId,
            _fixture.Clock.UtcNow));
        await db.SaveChangesAsync();

        db.Divisions.Add(Division.Provision(
            Guid.CreateVersion7(),
            countryId,
            4,
            "England",
            seasonId,
            _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_divisions_country_id_tier_number");
    }

    [Fact]
    public async Task A_world_cannot_have_two_seasons_with_the_same_number()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        // The arrangement already created sequence 1, so this one collides with it.
        var (worldId, _) = await ArrangeClubAsync(scope);
        var firstMatchday = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2027, 8, 1));

        db.Seasons.Add(Season.Create(
            Guid.CreateVersion7(),
            worldId,
            1,
            2027,
            "world-rules-v1",
            firstMatchday,
            _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_seasons_world_id_sequence_number");
    }

    [Fact]
    public async Task A_club_cannot_appear_twice_in_one_season()
    {
        // The constraint a promotion bug would otherwise violate silently, double-counting the club in
        // every standings query that follows.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (_, clubId) = await ArrangeClubAsync(scope);
        var countryId = await ReadCountryIdAsync(db, clubId);
        var seasonId = await db.Seasons.Select(season => season.Id).FirstAsync();

        var firstDivisionSeasonId = await db.DivisionSeasons.Select(entry => entry.Id).FirstAsync();

        var secondDivision = Division.Provision(Guid.CreateVersion7(), countryId, 5, "England", seasonId, now);
        db.Divisions.Add(secondDivision);
        await db.SaveChangesAsync();

        var secondDivisionSeason = DivisionSeason.Create(
            Guid.CreateVersion7(),
            secondDivision.Id,
            seasonId,
            "schedule-seed",
            "tie-seed",
            "tie-hash",
            now);
        db.DivisionSeasons.Add(secondDivisionSeason);
        await db.SaveChangesAsync();

        db.ClubSeasonEntries.Add(ClubSeasonEntry.Enter(
            Guid.CreateVersion7(),
            firstDivisionSeasonId,
            seasonId,
            clubId,
            ClubControlType.Ai,
            now));
        db.ClubSeasonEntries.Add(ClubSeasonEntry.Enter(
            Guid.CreateVersion7(),
            secondDivisionSeason.Id,
            seasonId,
            clubId,
            ClubControlType.Ai,
            now));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_club_season_entries_season_id_club_id");
    }

    [Fact]
    public async Task Reserved_funds_can_never_exceed_the_cash_behind_them()
    {
        // FIN-13, and the reason the reserved balance exists at all: a bid cannot promise money the club
        // does not have (FIN-10).
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, clubId) = await ArrangeClubAsync(scope);

        var account = ClubAccount.Open(Guid.CreateVersion7(), clubId, _fixture.Clock.UtcNow);
        db.ClubAccounts.Add(account);
        db.LedgerEntries.Add(account.Post(
            LedgerPostings.OpeningBalance(Guid.CreateVersion7(), clubId, 1_000),
            _fixture.Clock.UtcNow));
        await db.SaveChangesAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set reserved_minor = cash_minor + 1 where id = {0}",
            account.Id);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_club_accounts_reserved_within_cash");
    }

    [Fact]
    public async Task Cash_can_never_go_negative()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, clubId) = await ArrangeClubAsync(scope);

        var account = ClubAccount.Open(Guid.CreateVersion7(), clubId, _fixture.Clock.UtcNow);
        db.ClubAccounts.Add(account);
        db.LedgerEntries.Add(account.Post(
            LedgerPostings.OpeningBalance(Guid.CreateVersion7(), clubId, 500),
            _fixture.Clock.UtcNow));
        await db.SaveChangesAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set cash_minor = -1 where id = {0}",
            account.Id);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_club_accounts_cash_nonnegative");
    }

    [Fact]
    public async Task An_account_has_exactly_one_manager_profile()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var managerId = await CreateManagerAsync(scope);
        var manager = await db.Managers.SingleAsync(candidate => candidate.Id == managerId);

        db.Managers.Add(Manager.Create(
            Guid.CreateVersion7(),
            manager.UserId,
            "en-GB",
            "Europe/London",
            _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_managers_user_id");
    }

    [Fact]
    public async Task A_club_must_belong_to_a_country_that_exists()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        db.Clubs.Add(Club.Generate(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new ClubIdentity("Orphan Vale", "ORP", "Nowhere", "Nowhere", "seed-orphan"),
            tier: 1,
            foundingGameYear: 2026,
            now: _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_club_cannot_hold_two_buildings_of_one_kind()
    {
        // STRC-1: one snack bar and one shop. The unique index is what makes it a database fact.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (_, clubId) = await ArrangeClubAsync(scope);

        db.ClubStructures.Add(ClubStructure.Open(Guid.CreateVersion7(), clubId, StructureKind.SnackBar, now));
        await db.SaveChangesAsync();

        db.ClubStructures.Add(ClubStructure.Open(Guid.CreateVersion7(), clubId, StructureKind.SnackBar, now));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_club_structures_club_id_kind");
    }

    [Fact]
    public async Task A_building_above_the_top_level_is_refused_by_the_database()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, clubId) = await ArrangeClubAsync(scope);
        var building = ClubStructure.Open(Guid.CreateVersion7(), clubId, StructureKind.Merchandise, _fixture.Clock.UtcNow);
        db.ClubStructures.Add(building);
        await db.SaveChangesAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update world.club_structures set level = 11 where id = {0}",
            building.Id);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_club_structures_level");
    }

    [Fact]
    public async Task An_upgrade_that_skips_a_level_or_has_no_due_time_is_refused_by_the_database()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, clubId) = await ArrangeClubAsync(scope);
        var building = ClubStructure.Open(Guid.CreateVersion7(), clubId, StructureKind.SnackBar, _fixture.Clock.UtcNow);
        db.ClubStructures.Add(building);
        await db.SaveChangesAsync();

        var skipped = () => db.Database.ExecuteSqlRawAsync(
            "update world.club_structures set upgrade_target_level = 3, upgrade_due_at = now() where id = {0}",
            building.Id);
        var undated = () => db.Database.ExecuteSqlRawAsync(
            "update world.club_structures set upgrade_target_level = 2 where id = {0}",
            building.Id);

        (await skipped.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
            .Should().Be("ck_club_structures_upgrade");
        (await undated.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
            .Should().Be("ck_club_structures_upgrade");
    }

    [Fact]
    public async Task Two_orders_made_against_one_version_of_a_building_cannot_both_win()
    {
        // CONC-1: the version is the concurrency token, so the second order is refused rather than paid for.
        await using var first = _fixture.CreateScope();
        await using var second = _fixture.CreateScope();
        var firstDb = first.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (_, clubId) = await ArrangeClubAsync(first);
        var building = ClubStructure.Open(Guid.CreateVersion7(), clubId, StructureKind.SnackBar, now);
        firstDb.ClubStructures.Add(building);
        await firstDb.SaveChangesAsync();

        var mine = await firstDb.ClubStructures.SingleAsync(candidate => candidate.Id == building.Id);
        var theirs = await secondDb.ClubStructures.SingleAsync(candidate => candidate.Id == building.Id);

        mine.StartUpgrade(now.AddDays(1), now);
        theirs.StartUpgrade(now.AddDays(1), now);

        await firstDb.SaveChangesAsync();

        var act = async () => await secondDb.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task A_building_keeps_its_upgrade_state_and_history_rows_round_trip()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (_, clubId) = await ArrangeClubAsync(scope);
        var entryId = await OpenAccountAsync(db, clubId);
        var weekOf = new DateOnly(2026, 10, 18);

        var building = ClubStructure.Open(Guid.CreateVersion7(), clubId, StructureKind.Merchandise, now);
        building.StartUpgrade(now.AddDays(2), now);
        db.ClubStructures.Add(building);
        db.ClubStructureTakings.Add(ClubStructureTakings.WeekSale(
            Guid.CreateVersion7(), clubId, weekOf, level: 1, fans: 10_000, baseMinor: 300_000,
            bonusBp: 500, bonusReason: "calm", amountMinor: 315_000, entryId, now));
        db.ClubStructureTakings.Add(ClubStructureTakings.Upkeep(
            Guid.CreateVersion7(), clubId, StructureKind.Merchandise, weekOf, level: 1, upkeepMinor: 30_000, entryId, now));
        await db.SaveChangesAsync();

        await using var readScope = _fixture.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var stored = await readDb.ClubStructures.AsNoTracking().SingleAsync(candidate => candidate.Id == building.Id);
        stored.Kind.Should().Be(StructureKind.Merchandise);
        stored.Level.Should().Be(1);
        stored.UpgradeTargetLevel.Should().Be(2);
        stored.UpgradeDueAt.Should().BeCloseTo(now.AddDays(2), TimeSpan.FromMilliseconds(1));

        var rows = await readDb.ClubStructureTakings.AsNoTracking()
            .Where(row => row.ClubId == clubId)
            .OrderByDescending(row => row.AmountMinor)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].RowKind.Should().Be(StructureTakingsKind.WeekSale);
        rows[0].WeekOf.Should().Be(weekOf);
        rows[0].AmountMinor.Should().Be(315_000);
        rows[0].BonusReason.Should().Be("calm");
        rows[1].RowKind.Should().Be(StructureTakingsKind.Upkeep);
        rows[1].AmountMinor.Should().Be(-30_000, "upkeep is stored as the money it moved");
    }

    [Fact]
    public async Task A_buildings_week_cannot_be_booked_twice()
    {
        // A retried weekly run collides with its own first rows instead of booking the week again.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var (_, clubId) = await ArrangeClubAsync(scope);
        var entryId = await OpenAccountAsync(db, clubId);
        var weekOf = new DateOnly(2026, 10, 18);

        db.ClubStructureTakings.Add(ClubStructureTakings.Upkeep(
            Guid.CreateVersion7(), clubId, StructureKind.SnackBar, weekOf, 1, 30_000, entryId, now));
        await db.SaveChangesAsync();

        db.ClubStructureTakings.Add(ClubStructureTakings.Upkeep(
            Guid.CreateVersion7(), clubId, StructureKind.SnackBar, weekOf, 1, 30_000, entryId, now));

        var act = async () => await db.SaveChangesAsync();
        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_club_structure_takings_weekly");
    }

    [Fact]
    public async Task A_match_sale_must_name_a_fixture_and_be_the_snack_bars()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var (_, clubId) = await ArrangeClubAsync(scope);
        var entryId = await OpenAccountAsync(db, clubId);

        var act = () => db.Database.ExecuteSqlRawAsync(
            "insert into world.club_structure_takings "
            + "(id, club_id, kind, row_kind, fixture_id, week_of, level, customers, base_minor, bonus_bp, bonus_reason, "
            + "amount_minor, ledger_entry_id, created_at) "
            + "values ({0}, {1}, 'snack_bar', 'match_sale', null, '2026-10-18', 1, 100, 100, 0, 'none', 100, {2}, now())",
            Guid.CreateVersion7(),
            clubId,
            entryId);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_club_structure_takings_match_sale_shape");
    }

    /// <summary>Opens the club's account with a ledger entry, returning the entry's identity.</summary>
    private async Task<Guid> OpenAccountAsync(Tactical11ManagerDbContext db, Guid clubId)
    {
        var account = ClubAccount.Open(Guid.CreateVersion7(), clubId, _fixture.Clock.UtcNow);
        var entryId = Guid.CreateVersion7();
        db.ClubAccounts.Add(account);
        db.LedgerEntries.Add(account.Post(
            LedgerPostings.OpeningBalance(entryId, clubId, 1_000_000),
            _fixture.Clock.UtcNow));
        await db.SaveChangesAsync();

        return entryId;
    }

    /// <summary>Reads the constraint name out of a failed write.</summary>
    private static string? ConstraintOf(FluentAssertions.Specialized.ExceptionAssertions<DbUpdateException> exception) =>
        (exception.Which.InnerException as PostgresException)?.ConstraintName;

    private static string Key() => $"claim-{Guid.NewGuid():N}";

    private static async Task<Guid> ReadCountryIdAsync(Tactical11ManagerDbContext db, Guid clubId) =>
        await db.Clubs.Where(club => club.Id == clubId).Select(club => club.CountryId).SingleAsync();

    /// <summary>Creates a world, a country, a season and a club, returning the world and club identities.</summary>
    private async Task<(Guid WorldId, Guid ClubId)> ArrangeClubAsync(AsyncServiceScope scope, string name = "Vale United")
    {
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var worldId = Guid.CreateVersion7();
        db.GameWorlds.Add(GameWorld.Create(worldId, $"World {Guid.NewGuid():N}", now));
        await db.SaveChangesAsync();

        var countryId = Guid.CreateVersion7();
        db.Countries.Add(Country.Create(countryId, worldId, LaunchCountries.All[0], now));
        await db.SaveChangesAsync();

        // A country always has a running season in practice; the provisioning and entry tests need one.
        var firstMatchday = SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1));
        var seasonId = Guid.CreateVersion7();
        db.Seasons.Add(Season.Create(seasonId, worldId, 1, 2026, "world-rules-v1", firstMatchday, now));
        await db.SaveChangesAsync();

        var divisionId = Guid.CreateVersion7();
        db.Divisions.Add(Division.Provision(divisionId, countryId, 1, "England", seasonId, now));
        await db.SaveChangesAsync();

        var divisionSeasonId = Guid.CreateVersion7();
        db.DivisionSeasons.Add(DivisionSeason.Create(
            divisionSeasonId,
            divisionId,
            seasonId,
            "schedule-seed",
            "tie-seed",
            "tie-hash",
            now));
        await db.SaveChangesAsync();

        var clubId = await CreateClubAsync(scope, worldId, countryId, name);

        return (worldId, clubId);
    }

    private async Task<Guid> CreateClubAsync(
        AsyncServiceScope scope,
        Guid worldId,
        Guid countryId,
        string name)
    {
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = Guid.CreateVersion7();

        db.Clubs.Add(Club.Generate(
            clubId,
            worldId,
            countryId,
            new ClubIdentity(name, "SHO", "Vale", "Vale", $"seed-{clubId:N}"),
            tier: 1,
            foundingGameYear: 2026,
            now: _fixture.Clock.UtcNow));

        await db.SaveChangesAsync();

        return clubId;
    }

    private async Task<Guid> CreateManagerAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;
        var userId = Guid.CreateVersion7();
        var suffix = Guid.NewGuid().ToString("N");

        db.Users.Add(User.Register(userId, $"{suffix}@example.com", $"Manager {suffix}"[..24], "hash", "stamp", now));
        await db.SaveChangesAsync();

        var managerId = Guid.CreateVersion7();
        db.Managers.Add(Manager.Create(managerId, userId, "en-GB", "Europe/London", now));
        await db.SaveChangesAsync();

        return managerId;
    }
}

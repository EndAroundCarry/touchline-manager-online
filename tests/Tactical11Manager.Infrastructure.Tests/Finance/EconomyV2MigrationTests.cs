using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.Finance;

/// <summary>
/// The <c>EconomyV2Persistence</c> migration's conversion of wages from a week to a season, the standing it opens in hundredths, and the
/// rollback, against real PostgreSQL (`MIG-1`…`MIG-7`, plan toasty-scott milestone 2).
/// </summary>
/// <remarks>
/// Runs in a database of its own so that stepping the schema back and forth cannot disturb the collection's shared one. Foreign keys are
/// switched off for the session that inserts the rows, because the point is the column transformation and not the world those rows would
/// normally hang from.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class EconomyV2MigrationTests
{
    private const string Before = "20261022070519_RaiseStadiumCapacityTo75000";
    private const string After = "20261022073638_EconomyV2Persistence";

    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public EconomyV2MigrationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_weekly_wage_becomes_a_season_wage_and_a_standing_opens_in_hundredths()
    {
        var connectionString = await CreateDatabaseAsync();

        await using var provider = BuildProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var migrator = db.GetService<IMigrator>();

        // One physical connection throughout: the session setting below is lost when a connection returns to the pool.
        await db.Database.OpenConnectionAsync();

        await migrator.MigrateAsync(Before);

        var clubId = Guid.CreateVersion7();

        await db.Database.ExecuteSqlRawAsync("set session_replication_role = replica");
        await InsertClubAsync(db, clubId, reputation: 63);
        await InsertAccountAsync(db, clubId, cash: 5_000);
        await InsertContractAsync(db, clubId, wage: 300, status: "active", closedReason: null);
        await InsertContractAsync(db, clubId, wage: 300, status: "closed", closedReason: "renewed");
        await InsertListingAsync(db, clubId, buyerWage: 600);

        await migrator.MigrateAsync(After);

        (await ScalarAsync<long>(db, "select max(season_wage_minor) as \"Value\" from squad.player_contracts where status = 'active'"))
            .Should().Be(3_400, "a weekly run settled three matchdays of a season of 34, so a week of 300 is a season of 3,400");
        (await ScalarAsync<long>(db, "select max(season_wage_minor) as \"Value\" from squad.player_contracts where status = 'closed'"))
            .Should().Be(3_400, "closed contracts are converted too, so history reads in the units of the column");
        (await ScalarAsync<long>(db, "select generated_buyer_season_wage_minor as \"Value\" from market.transfer_listings"))
            .Should().Be(6_800);
        (await ScalarAsync<int>(db, "select standing_centis as \"Value\" from world.clubs"))
            .Should().Be(6_300, "the reputation of 63 points opens as 6,300 hundredths");
        (await ScalarAsync<string>(db, "select financial_state as \"Value\" from finance.club_accounts"))
            .Should().Be("healthy");
        (await ScalarAsync<long>(db, "select overdraft_floor_minor as \"Value\" from finance.club_accounts"))
            .Should().Be(0, "an account that has signed no sponsor deal behaves as before: cash never below zero");
    }

    [Fact]
    public async Task Rolling_back_restores_a_weekly_wage()
    {
        var connectionString = await CreateDatabaseAsync();

        await using var provider = BuildProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var migrator = db.GetService<IMigrator>();

        await db.Database.OpenConnectionAsync();

        await migrator.MigrateAsync(After);

        var clubId = Guid.CreateVersion7();

        await db.Database.ExecuteSqlRawAsync("set session_replication_role = replica");
        await InsertClubAsync(db, clubId, reputation: 50, standingCentis: 5_000);
        await InsertAccountAsync(db, clubId, cash: 5_000, financialState: "healthy");
        await InsertContractAsync(db, clubId, wage: 3_400, status: "active", closedReason: null, seasonColumn: true);
        await InsertListingAsync(db, clubId, buyerWage: 6_800, seasonColumn: true);

        await migrator.MigrateAsync(Before);

        (await ScalarAsync<long>(db, "select weekly_wage_minor as \"Value\" from squad.player_contracts"))
            .Should().Be(300);
        (await ScalarAsync<long>(db, "select generated_buyer_wage_minor as \"Value\" from market.transfer_listings"))
            .Should().Be(600);
    }

    private static async Task InsertClubAsync(Tactical11ManagerDbContext db, Guid clubId, int reputation, int? standingCentis = null)
    {
        var slug = $"club-{clubId:N}";

        if (standingCentis is { } centis)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into world.clubs
                    (id, world_id, country_id, name, normalized_name, short_name, slug, city, region, badge_seed, founding_game_year,
                     status, reputation, standing_centis, created_at, updated_at, version)
                values ({clubId}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {slug}, {slug.ToUpperInvariant()}, 'TST', {slug},
                        'Town', 'Region', 'seed', 1950, 'active', {reputation}, {centis}, now(), now(), 1)
                """);

            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into world.clubs
                (id, world_id, country_id, name, normalized_name, short_name, slug, city, region, badge_seed, founding_game_year,
                 status, reputation, created_at, updated_at, version)
            values ({clubId}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {slug}, {slug.ToUpperInvariant()}, 'TST', {slug},
                    'Town', 'Region', 'seed', 1950, 'active', {reputation}, now(), now(), 1)
            """);
    }

    private static async Task InsertAccountAsync(Tactical11ManagerDbContext db, Guid clubId, long cash, string? financialState = null)
    {
        if (financialState is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into finance.club_accounts
                    (id, club_id, cash_minor, reserved_minor, last_ledger_sequence, financial_state, state_since_matchday,
                     overdraft_limit_minor, overdraft_floor_minor, created_at, updated_at, version)
                values ({Guid.CreateVersion7()}, {clubId}, {cash}, 0, 0, {financialState}, 0, 0, 0, now(), now(), 1)
                """);

            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into finance.club_accounts
                (id, club_id, cash_minor, reserved_minor, last_ledger_sequence, created_at, updated_at, version)
            values ({Guid.CreateVersion7()}, {clubId}, {cash}, 0, 0, now(), now(), 1)
            """);
    }

    private static async Task InsertContractAsync(
        Tactical11ManagerDbContext db,
        Guid clubId,
        long wage,
        string status,
        string? closedReason,
        bool seasonColumn = false)
    {
        var closedAt = closedReason is null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow;

        if (seasonColumn)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into squad.player_contracts
                    (id, player_id, club_id, start_season_number, end_season_number, season_wage_minor, squad_status, status,
                     closed_reason, closed_at, created_at, updated_at, version)
                values ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {clubId}, 1, 2, {wage}, 'first_team', {status},
                        {closedReason}, {closedAt}, now(), now(), 1)
                """);

            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into squad.player_contracts
                (id, player_id, club_id, start_season_number, end_season_number, weekly_wage_minor, squad_status, status,
                 closed_reason, closed_at, created_at, updated_at, version)
            values ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {clubId}, 1, 2, {wage}, 'first_team', {status},
                    {closedReason}, {closedAt}, now(), now(), 1)
            """);
    }

    private static async Task InsertListingAsync(Tactical11ManagerDbContext db, Guid clubId, long buyerWage, bool seasonColumn = false)
    {
        if (seasonColumn)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into market.transfer_listings
                    (id, player_id, seller_club_id, minimum_fee_minor, generated_buyer_season_wage_minor, generated_contract_seasons,
                     opens_at, ends_at, status, created_at, updated_at, version)
                values ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {clubId}, 1000, {buyerWage}, 2,
                        now(), now() + interval '1 day', 'open', now(), now(), 1)
                """);

            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into market.transfer_listings
                (id, player_id, seller_club_id, minimum_fee_minor, generated_buyer_wage_minor, generated_contract_seasons,
                 opens_at, ends_at, status, created_at, updated_at, version)
            values ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {clubId}, 1000, {buyerWage}, 2,
                    now(), now() + interval '1 day', 'open', now(), now(), 1)
            """);
    }

    private static async Task<T> ScalarAsync<T>(Tactical11ManagerDbContext db, string sqlAliasedAsValue) =>
        (await db.Database.SqlQueryRaw<T>(sqlAliasedAsValue).ToListAsync()).Single();

    private async Task<string> CreateDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = $"economy_migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var admin = new NpgsqlConnection(_fixture.ConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand(
            $"create database \"{new NpgsqlConnectionStringBuilder(connectionString).Database}\"",
            admin);
        await create.ExecuteNonQueryAsync();

        return connectionString;
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = connectionString,
                ["Auth:SigningKey"] = "test-only-signing-key-with-at-least-32-bytes",
                ["Auth:EncryptionKey"] = "test-only-encryption-key-with-at-least-32-bytes",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }
}

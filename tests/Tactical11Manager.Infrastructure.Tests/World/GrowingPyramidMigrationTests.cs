using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.World;

/// <summary>
/// The <c>GrowingPyramidLeagues</c> migration's backfill, new unique keys and rollback, against real PostgreSQL
/// (`PYR-3`, `PYR-15`).
/// </summary>
/// <remarks>
/// Runs in a database of its own so that stepping the schema back and forth cannot disturb the collection's shared
/// one. Foreign keys are switched off for the session that inserts the old rows, because the point is the column
/// transformation and not the world those rows would normally hang from.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class GrowingPyramidMigrationTests
{
    private const string Before = "20261022215224_EleventhPlaceObjective";
    private const string After = "20261023161838_GrowingPyramidLeagues";

    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public GrowingPyramidMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Each_old_tier_becomes_league_one_fed_into_the_tier_above_and_rolling_back_restores_the_names()
    {
        var connectionString = await CreateDatabaseAsync();

        await using var provider = BuildProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var migrator = db.GetService<IMigrator>();

        // One physical connection throughout: the session setting below is lost when a connection returns to the pool.
        await db.Database.OpenConnectionAsync();

        await migrator.MigrateAsync(Before);

        var countryId = Guid.CreateVersion7();
        var seasonId = Guid.CreateVersion7();
        var top = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var third = Guid.CreateVersion7();
        var requestId = Guid.CreateVersion7();

        await db.Database.ExecuteSqlRawAsync("set session_replication_role = replica");
        await InsertDivisionAsync(db, top, countryId, seasonId, 1, "England Top Division");
        await InsertDivisionAsync(db, second, countryId, seasonId, 2, "England Division 2");
        await InsertDivisionAsync(db, third, countryId, seasonId, 3, "England Division 3");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into world.division_provisioning_requests
                (id, country_id, target_tier, target_season_id, status, generation_seed, requested_at, created_at, updated_at, version)
            values ({requestId}, {countryId}, 3, {seasonId}, 'completed', 'seed', now(), now(), now(), 1)
            """);

        await migrator.MigrateAsync(After);

        var divisions = await db.Database
            .SqlQuery<DivisionRow>(
                $"select id as \"Id\", league_number as \"LeagueNumber\", parent_division_id as \"ParentId\", display_name as \"Name\" from competition.divisions")
            .ToDictionaryAsync(row => row.Id);

        divisions[top].ParentId.Should().BeNull("the top division feeds nothing");
        divisions[second].ParentId.Should().Be(top, "tier 2 fed tier 1");
        divisions[third].ParentId.Should().Be(second, "tier 3 fed tier 2");
        divisions.Values.Should().OnlyContain(row => row.LeagueNumber == 1);
        divisions[top].Name.Should().Be("England Top Division");
        divisions[second].Name.Should().Be("England Division 2.1");
        divisions[third].Name.Should().Be("England Division 3.1");

        var request = await db.Database
            .SqlQuery<RequestRow>(
                $"select target_league_number as \"LeagueNumber\", parent_division_id as \"ParentId\" from world.division_provisioning_requests")
            .SingleAsync();

        request.LeagueNumber.Should().Be(1);
        request.ParentId.Should().Be(second, "the request for tier 3 aimed below tier 2");

        // The unique keys now hold a league: a second league of a tier fits, a repeat of one does not.
        await InsertDivisionAsync(db, Guid.CreateVersion7(), countryId, seasonId, 3, "England Division 3.2", leagueNumber: 2, parentId: second);

        var repeat = () => InsertDivisionAsync(
            db, Guid.CreateVersion7(), countryId, seasonId, 3, "England Division 3.2 again", leagueNumber: 2, parentId: second);

        (await repeat.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ux_divisions_country_id_tier_number_league_number");

        var secondRequest = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into world.division_provisioning_requests
                (id, country_id, target_tier, target_league_number, parent_division_id, target_season_id, status, generation_seed, requested_at, created_at, updated_at, version)
            values ({Guid.CreateVersion7()}, {countryId}, 3, 1, {second}, {seasonId}, 'requested', 'seed', now(), now(), now(), 1)
            """);

        (await secondRequest.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ux_division_provisioning_requests_country_target_league");

        // Rolling back keeps league 1 of each tier and puts the old names back.
        await db.Database.ExecuteSqlRawAsync("delete from competition.divisions where league_number = 2");
        await migrator.MigrateAsync(Before);

        (await db.Database
            .SqlQueryRaw<string>("select display_name as \"Value\" from competition.divisions order by tier_number")
            .ToListAsync())
            .Should().Equal("England Top Division", "England Division 2", "England Division 3");
    }

    private static Task<int> InsertDivisionAsync(
        Tactical11ManagerDbContext db,
        Guid id,
        Guid countryId,
        Guid seasonId,
        int tier,
        string name,
        int? leagueNumber = null,
        Guid? parentId = null) =>
        leagueNumber is null
            ? db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into competition.divisions
                    (id, country_id, tier_number, display_name, status, created_season_id, capacity, created_at, updated_at, version)
                values ({id}, {countryId}, {tier}, {name}, 'active', {seasonId}, 16, now(), now(), 1)
                """)
            : db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into competition.divisions
                    (id, country_id, tier_number, league_number, parent_division_id, display_name, status, created_season_id, capacity, created_at, updated_at, version)
                values ({id}, {countryId}, {tier}, {leagueNumber}, {parentId}, {name}, 'active', {seasonId}, 16, now(), now(), 1)
                """);

    private async Task<string> CreateDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = $"pyramid_migration_{Guid.NewGuid():N}",
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

    private sealed record DivisionRow(Guid Id, int LeagueNumber, Guid? ParentId, string Name);

    private sealed record RequestRow(int LeagueNumber, Guid ParentId);
}

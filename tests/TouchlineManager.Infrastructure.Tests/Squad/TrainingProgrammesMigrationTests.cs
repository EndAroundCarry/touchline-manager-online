using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Squad;

/// <summary>
/// The <c>TrainingProgrammesAndAging</c> migration's backfill and rollback, against real PostgreSQL
/// (`MIG-1`…`MIG-7`).
/// </summary>
/// <remarks>
/// Runs in a database of its own so that stepping the schema back and forth cannot disturb the collection's
/// shared one. Foreign keys are switched off for the session that inserts the legacy rows, because the point
/// is the column transformation and not the world those rows would normally hang from.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TrainingProgrammesMigrationTests
{
    private const string Before = "20261003190000_MoreFormationPresets";
    private const string After = "20261004064854_TrainingProgrammesAndAging";

    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public TrainingProgrammesMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Legacy_family_focuses_move_onto_programmes_and_rolling_back_restores_a_family()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = $"training_migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand(
                $"create database \"{new NpgsqlConnectionStringBuilder(connectionString).Database}\"",
                admin);
            await create.ExecuteNonQueryAsync();
        }

        await using var provider = BuildProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var migrator = db.GetService<IMigrator>();

        // One physical connection throughout: the session setting below is lost when a connection returns to
        // the pool.
        await db.Database.OpenConnectionAsync();

        await migrator.MigrateAsync(Before);

        var families = new[] { "technical", "mental", "physical", "goalkeeping" };

        foreach (var family in families)
        {
            await InsertLegacyFocusAsync(db, family);
        }

        await migrator.MigrateAsync(After);

        var migrated = await ReadAsync(db);

        migrated.Should().Contain(("mental", "mental"), "a mental focus becomes the mental programme");
        migrated.Should().Contain(("physical", "physical"), "a physical focus becomes the physical programme");
        migrated.Should().Contain(("goalkeeping", "goalkeeper"), "a goalkeeping focus becomes the goalkeeper programme");
        migrated.Should().Contain(
            ("technical", null),
            "a technical focus has no programme counterpart, so the player returns to the position default");
        migrated.Should().HaveCount(families.Length, "the expand step deletes nothing");

        // A row written after the migration holds only a programme; rolling back must still satisfy the
        // restored not-null family column.
        await db.Database.ExecuteSqlRawAsync("set session_replication_role = replica");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into squad.player_training_focus
                (id, player_id, club_id, focus_family, programme, effective_date, created_at, updated_at, version)
            values ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, null, 'forward',
                    {new DateOnly(2026, 10, 1)}, now(), now(), 1)
            """);

        await migrator.MigrateAsync(Before);

        var rolledBack = await db.Database
            .SqlQueryRaw<string>("select focus_family as \"Value\" from squad.player_training_focus")
            .ToListAsync();

        rolledBack.Should().HaveCount(families.Length + 1);
        rolledBack.Should().OnlyContain(family => families.Contains(family), "every row has a legacy family again");
    }

    private static async Task InsertLegacyFocusAsync(TouchlineManagerDbContext db, string family)
    {
        await db.Database.ExecuteSqlRawAsync("set session_replication_role = replica");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into squad.player_training_focus
                (id, player_id, club_id, focus_family, effective_date, created_at, updated_at, version)
            values ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {family},
                    {new DateOnly(2026, 10, 1)}, now(), now(), 1)
            """);
    }

    private static async Task<List<(string Family, string? Programme)>> ReadAsync(TouchlineManagerDbContext db)
    {
        var connection = db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "select focus_family, programme from squad.player_training_focus";

        var rows = new List<(string, string?)>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return rows;
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

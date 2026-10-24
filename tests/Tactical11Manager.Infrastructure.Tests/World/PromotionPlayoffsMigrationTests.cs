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
/// The <c>PromotionPlayoffs</c> migration against real PostgreSQL: the matchday kind it adds, the play-off tie table with
/// its checks and unique keys, and the way back (`PR-4`).
/// </summary>
/// <remarks>
/// <para>
/// The migration runs in a database of its own so that stepping the schema back and forth cannot disturb the collection's
/// shared one. Foreign keys are switched off for the session that inserts, because the point is the table's own rules and
/// not the world its rows would hang from; the foreign keys are generated from the model and the matchday tests exercise
/// them with a real world.
/// </para>
/// <para>
/// The constraint cases run against the collection's migrated database, each in a transaction that is rolled back, so the
/// shared database is left as it was found.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PromotionPlayoffsMigrationTests
{
    private const string Before = "20261023161838_GrowingPyramidLeagues";
    private const string After = "20261023222716_PromotionPlayoffs";

    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public PromotionPlayoffsMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Existing_rounds_become_league_rounds_and_rolling_back_drops_the_kind_and_the_ties()
    {
        var connectionString = await CreateDatabaseAsync();

        await using var provider = BuildProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var migrator = db.GetService<IMigrator>();

        // One physical connection throughout: the session setting below is lost when a connection returns to the pool.
        await db.Database.OpenConnectionAsync();

        await migrator.MigrateAsync(Before);

        var leagueRound = Guid.CreateVersion7();
        var divisionSeasonId = Guid.CreateVersion7();

        await db.Database.ExecuteSqlRawAsync("set session_replication_role = replica");
        await InsertMatchdayAsync(db, leagueRound, divisionSeasonId, 7, "pending");

        await migrator.MigrateAsync(After);

        (await db.Database
            .SqlQuery<string>($"select kind as \"Value\" from competition.matchdays where id = {leagueRound}")
            .SingleAsync())
            .Should().Be("league", "every round before play-offs existed is a league round");

        var playoffRound = Guid.CreateVersion7();

        await InsertMatchdayAsync(db, playoffRound, divisionSeasonId, 31, "pending", kind: "playoff");

        (await db.Database
            .SqlQuery<string>($"select kind as \"Value\" from competition.matchdays where id = {playoffRound}")
            .SingleAsync())
            .Should().Be("playoff");

        var unknownKind = () => InsertMatchdayAsync(db, Guid.CreateVersion7(), divisionSeasonId, 32, "pending", kind: "friendly");

        (await unknownKind.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_matchdays_kind");

        (await db.Database
            .SqlQuery<string>($"select to_regclass('competition.playoff_ties')::text as \"Value\"")
            .SingleAsync())
            .Should().Be("competition.playoff_ties");

        // Rolling back takes the table and the column away; the rounds themselves stay.
        await db.Database.ExecuteSqlRawAsync("delete from competition.matchdays where kind = 'playoff'");
        await migrator.MigrateAsync(Before);

        (await db.Database
            .SqlQuery<string?>($"select to_regclass('competition.playoff_ties')::text as \"Value\"")
            .SingleAsync())
            .Should().BeNull();

        (await db.Database
            .SqlQuery<int>(
                $"select count(*)::int as \"Value\" from information_schema.columns where table_schema = 'competition' and table_name = 'matchdays' and column_name = 'kind'")
            .SingleAsync())
            .Should().Be(0);

        (await db.Database
            .SqlQuery<int>($"select count(*)::int as \"Value\" from competition.matchdays where id = {leagueRound}")
            .SingleAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task A_semi_final_and_a_final_waiting_for_its_away_side_are_valid_ties()
    {
        (await TryInsertAsync(new Dictionary<string, object?>())).Should().BeNull();

        (await TryInsertAsync(new Dictionary<string, object?>
        {
            ["stage"] = "final",
            ["slot"] = 2,
            ["away_club_id"] = null,
            ["away_division_id"] = null,
            ["away_place"] = null,
        })).Should().BeNull();
    }

    [Fact]
    public async Task A_tie_decided_on_penalties_keeps_both_shoot_out_scores()
    {
        var winner = Guid.CreateVersion7();
        var loser = Guid.CreateVersion7();

        (await TryInsertAsync(new Dictionary<string, object?>
        {
            ["home_club_id"] = winner,
            ["away_club_id"] = loser,
            ["winner_club_id"] = winner,
            ["loser_club_id"] = loser,
            ["decided_by"] = "penalties",
            ["shootout_home_score"] = 4,
            ["shootout_away_score"] = 3,
        })).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(RefusedTies))]
    public async Task A_tie_that_breaks_a_rule_is_refused_by_the_database(
        string description,
        string constraint,
        Dictionary<string, object?> overrides)
    {
        (await TryInsertAsync(overrides)).Should().Be(constraint, description);
    }

    [Fact]
    public async Task A_stage_is_drawn_once_per_feeder_league_even_when_the_feeder_is_absent()
    {
        var season = Guid.CreateVersion7();
        var parent = Guid.CreateVersion7();
        var feeder = Guid.CreateVersion7();

        var sameSemiFinal = await TryInsertAsync(
            new Dictionary<string, object?> { ["season_id"] = season, ["parent_division_id"] = parent, ["feeder_division_id"] = feeder },
            new Dictionary<string, object?> { ["season_id"] = season, ["parent_division_id"] = parent, ["feeder_division_id"] = feeder });

        sameSemiFinal.Should().Be("ux_playoff_ties_season_parent_stage_feeder");

        var waiting = new Dictionary<string, object?>
        {
            ["season_id"] = season,
            ["parent_division_id"] = parent,
            ["feeder_division_id"] = null,
            ["stage"] = "final",
            ["slot"] = 2,
            ["away_club_id"] = null,
            ["away_division_id"] = null,
            ["away_place"] = null,
        };

        (await TryInsertAsync(waiting, new Dictionary<string, object?>(waiting)))
            .Should().Be("ux_playoff_ties_season_parent_stage_feeder", "an absent feeder counts as a value, so a second final is refused");

        (await TryInsertAsync(
            new Dictionary<string, object?> { ["season_id"] = season, ["parent_division_id"] = parent, ["feeder_division_id"] = feeder },
            new Dictionary<string, object?> { ["season_id"] = season, ["parent_division_id"] = parent, ["feeder_division_id"] = Guid.CreateVersion7() }))
            .Should().BeNull("a second feeder league of the same parent has its own semi-final");
    }

    [Fact]
    public async Task A_fixture_belongs_to_one_tie()
    {
        var fixtureId = Guid.CreateVersion7();

        (await TryInsertAsync(
            new Dictionary<string, object?> { ["fixture_id"] = fixtureId },
            new Dictionary<string, object?> { ["fixture_id"] = fixtureId, ["stage"] = "final", ["slot"] = 2 }))
            .Should().Be("ux_playoff_ties_fixture_id");

        (await TryInsertAsync(
            new Dictionary<string, object?> { ["fixture_id"] = null },
            new Dictionary<string, object?> { ["fixture_id"] = null, ["stage"] = "final", ["slot"] = 2 }))
            .Should().BeNull("a tie that is not scheduled yet has no fixture");
    }

    /// <summary>The ties the database refuses, each with the constraint that refuses it.</summary>
    public static IEnumerable<object[]> RefusedTies()
    {
        var home = Guid.CreateVersion7();
        var away = Guid.CreateVersion7();
        var stranger = Guid.CreateVersion7();

        yield return Case("an unknown stage", "ck_playoff_ties_stage", ("stage", "third_place"));
        yield return Case("an unknown decision", "ck_playoff_ties_decided_by", ("decided_by", "coin_toss"));
        yield return Case("a fourth slot", "ck_playoff_ties_slot", ("slot", 4));
        yield return Case("a slot of nought", "ck_playoff_ties_slot", ("slot", 0));
        yield return Case("a home place of nought", "ck_playoff_ties_places", ("home_place", 0));
        yield return Case("a club against itself", "ck_playoff_ties_distinct_clubs", ("home_club_id", home), ("away_club_id", home));
        yield return Case("an away club with no league", "ck_playoff_ties_away_side", ("away_division_id", null));
        yield return Case("an away league with no place", "ck_playoff_ties_away_side", ("away_place", null));
        yield return Case(
            "a semi-final with no away club",
            "ck_playoff_ties_semi_final_sides",
            ("away_club_id", null),
            ("away_division_id", null),
            ("away_place", null));
        yield return Case(
            "a fixture before the away club is known",
            "ck_playoff_ties_fixture_needs_sides",
            ("stage", "final"),
            ("slot", 2),
            ("away_club_id", null),
            ("away_division_id", null),
            ("away_place", null),
            ("fixture_id", Guid.CreateVersion7()));
        yield return Case(
            "a winner who did not play",
            "ck_playoff_ties_winner",
            ("home_club_id", home),
            ("away_club_id", away),
            ("winner_club_id", stranger),
            ("loser_club_id", home),
            ("decided_by", "score"));
        yield return Case(
            "a loser who did not play",
            "ck_playoff_ties_loser",
            ("home_club_id", home),
            ("away_club_id", away),
            ("winner_club_id", home),
            ("loser_club_id", stranger),
            ("decided_by", "score"));
        yield return Case(
            "a club that wins and loses",
            "ck_playoff_ties_loser",
            ("home_club_id", home),
            ("away_club_id", away),
            ("winner_club_id", home),
            ("loser_club_id", home),
            ("decided_by", "score"));
        yield return Case(
            "a winner with no loser",
            "ck_playoff_ties_decision",
            ("home_club_id", home),
            ("away_club_id", away),
            ("winner_club_id", home),
            ("decided_by", "score"));
        yield return Case(
            "a decision with no winner",
            "ck_playoff_ties_decision",
            ("decided_by", "score"));
        yield return Case(
            "a shoot-out score for one side only",
            "ck_playoff_ties_shootout_score",
            ("home_club_id", home),
            ("away_club_id", away),
            ("winner_club_id", home),
            ("loser_club_id", away),
            ("decided_by", "penalties"),
            ("shootout_home_score", 4));
        yield return Case(
            "a negative shoot-out score",
            "ck_playoff_ties_shootout_score",
            ("home_club_id", home),
            ("away_club_id", away),
            ("winner_club_id", home),
            ("loser_club_id", away),
            ("decided_by", "penalties"),
            ("shootout_home_score", 4),
            ("shootout_away_score", -1));
        yield return Case(
            "a penalty decision with no shoot-out score",
            "ck_playoff_ties_shootout_decision",
            ("home_club_id", home),
            ("away_club_id", away),
            ("winner_club_id", home),
            ("loser_club_id", away),
            ("decided_by", "penalties"));
        yield return Case(
            "a shoot-out score on a tie decided by the score",
            "ck_playoff_ties_shootout_decision",
            ("home_club_id", home),
            ("away_club_id", away),
            ("winner_club_id", home),
            ("loser_club_id", away),
            ("decided_by", "score"),
            ("shootout_home_score", 4),
            ("shootout_away_score", 3));
    }

    private static object[] Case(string description, string constraint, params (string Column, object? Value)[] changes) =>
        [description, constraint, changes.ToDictionary(change => change.Column, change => change.Value)];

    /// <summary>
    /// Inserts one valid semi-final with the given columns changed, then the second row when there is one, all in a
    /// transaction that is rolled back.
    /// </summary>
    /// <returns>The constraint that refused a row, or null when every row went in.</returns>
    private async Task<string?> TryInsertAsync(params Dictionary<string, object?>[] rows)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var relax = new NpgsqlCommand("set local session_replication_role = replica", connection, transaction))
        {
            await relax.ExecuteNonQueryAsync();
        }

        try
        {
            foreach (var overrides in rows)
            {
                var values = new Dictionary<string, object?>
                {
                    ["id"] = Guid.CreateVersion7(),
                    ["season_id"] = Guid.CreateVersion7(),
                    ["country_id"] = Guid.CreateVersion7(),
                    ["parent_division_id"] = Guid.CreateVersion7(),
                    ["feeder_division_id"] = Guid.CreateVersion7(),
                    ["stage"] = "semi_final",
                    ["slot"] = 1,
                    ["home_club_id"] = Guid.CreateVersion7(),
                    ["home_division_id"] = Guid.CreateVersion7(),
                    ["home_place"] = 3,
                    ["away_club_id"] = Guid.CreateVersion7(),
                    ["away_division_id"] = Guid.CreateVersion7(),
                    ["away_place"] = 4,
                    ["created_at"] = DateTimeOffset.UtcNow,
                    ["updated_at"] = DateTimeOffset.UtcNow,
                    ["version"] = 1L,
                };

                foreach (var (column, value) in overrides)
                {
                    values[column] = value;
                }

                var columns = string.Join(", ", values.Keys);
                var parameters = string.Join(", ", values.Keys.Select(column => "@" + column));

                await using var insert = new NpgsqlCommand(
                    $"insert into competition.playoff_ties ({columns}) values ({parameters})",
                    connection,
                    transaction);

                foreach (var (column, value) in values)
                {
                    insert.Parameters.AddWithValue(column, value ?? DBNull.Value);
                }

                await insert.ExecuteNonQueryAsync();
            }

            return null;
        }
        catch (PostgresException exception)
        {
            return exception.ConstraintName;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static Task<int> InsertMatchdayAsync(
        Tactical11ManagerDbContext db,
        Guid id,
        Guid divisionSeasonId,
        int round,
        string status,
        string? kind = null) =>
        kind is null
            ? db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into competition.matchdays
                    (id, division_season_id, round_number, lock_at, kickoff_at, publication_status, created_at, updated_at, version)
                values ({id}, {divisionSeasonId}, {round}, now(), now() + interval '1 hour', {status}, now(), now(), 1)
                """)
            : db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into competition.matchdays
                    (id, division_season_id, round_number, kind, lock_at, kickoff_at, publication_status, created_at, updated_at, version)
                values ({id}, {divisionSeasonId}, {round}, {kind}, now(), now() + interval '1 hour', {status}, now(), now(), 1)
                """);

    private async Task<string> CreateDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = $"playoff_migration_{Guid.NewGuid():N}",
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

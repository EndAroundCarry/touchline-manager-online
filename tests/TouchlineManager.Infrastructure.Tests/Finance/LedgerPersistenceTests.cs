using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Finance;

/// <summary>
/// The ledger's database-level guarantees over a real seeded world (master plan §15.2): every balance is the
/// running total of the entries behind it (`FIN-18`), and the constraints are what make an entry written
/// once (`FIN-11`, `FIN-17`).
/// </summary>
/// <remarks>
/// It reads the world <see cref="WorldFixture"/> seeds rather than building its own, because the property it
/// proves — that a real generation's balances replay from a real ledger — is about the seeded world itself.
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class LedgerPersistenceTests
{
    private readonly WorldFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public LedgerPersistenceTests(WorldFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Every_account_is_opened_by_exactly_one_entry_that_states_its_balance()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var accounts = await db.ClubAccounts.ToListAsync();
        var openings = await db.LedgerEntries
            .Where(entry => entry.Category == LedgerCategory.OpeningBalance)
            .ToListAsync();

        accounts.Should().NotBeEmpty("the fixture seeded a world (WORLD-5)");

        foreach (var account in accounts)
        {
            var ledger = openings.Where(entry => entry.ClubId == account.ClubId).ToList();

            ledger.Should().ContainSingle("a club's account is funded exactly once (FIN-1)");
            ledger[0].Sequence.Should().Be(1, "the opening balance is the first thing that happened");
            ledger[0].ResultingCashMinor.Should().Be(account.CashMinor);
            ledger[0].ResultingReservedMinor.Should().Be(0);
            ledger[0].SourceType.Should().Be(LedgerSourceType.WorldSeed);
        }
    }

    [Fact]
    public async Task The_ledger_replays_to_the_stored_balances()
    {
        // FIN-18: the account is a projection of the ledger, so summing its entries must reproduce the row
        // exactly — otherwise one of the two is wrong and a manager cannot tell which.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var accounts = await db.ClubAccounts.ToListAsync();
        var entries = await db.LedgerEntries.ToListAsync();

        accounts.Should().NotBeEmpty();

        foreach (var account in accounts)
        {
            var ledger = entries.Where(entry => entry.ClubId == account.ClubId).ToList();

            ledger.Should().NotBeEmpty();
            ledger.Sum(entry => entry.CashDeltaMinor).Should().Be(account.CashMinor);
            ledger.Sum(entry => entry.ReservedDeltaMinor).Should().Be(account.ReservedMinor);
            ledger.Max(entry => entry.Sequence).Should().Be(account.LastLedgerSequence);
        }
    }

    [Fact]
    public async Task A_clubs_ledger_sequence_is_unique()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (account, opening) = await PickAccountAsync(db);

        db.LedgerEntries.Add(LedgerEntry.Record(
            Guid.CreateVersion7(),
            account.ClubId,
            sequence: opening.Sequence,
            LedgerCategory.Compensation,
            cashDeltaMinor: 1,
            reservedDeltaMinor: 0,
            resultingCashMinor: account.CashMinor + 1,
            resultingReservedMinor: 0,
            LedgerSourceType.AdminRepair,
            sourceId: null,
            correlationId: $"duplicate-sequence-{Guid.NewGuid():N}",
            descriptionTemplate: opening.DescriptionTemplate,
            descriptionParametersJson: "{}",
            _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_ledger_entries_club_sequence");
    }

    [Fact]
    public async Task One_operation_posts_at_most_one_entry_per_category()
    {
        // FIN-17: a retried operation carries the same correlation key and category, and the index is what
        // stops the second write rather than a lock or an application check that a race could evade.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (account, opening) = await PickAccountAsync(db);

        db.LedgerEntries.Add(LedgerEntry.Record(
            Guid.CreateVersion7(),
            account.ClubId,
            sequence: account.LastLedgerSequence + 1,
            LedgerCategory.OpeningBalance,
            cashDeltaMinor: 1,
            reservedDeltaMinor: 0,
            resultingCashMinor: account.CashMinor + 1,
            resultingReservedMinor: 0,
            LedgerSourceType.WorldSeed,
            sourceId: account.ClubId,
            correlationId: opening.CorrelationId,
            descriptionTemplate: opening.DescriptionTemplate,
            descriptionParametersJson: "{}",
            _fixture.Clock.UtcNow));

        var act = async () => await db.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        ConstraintOf(exception).Should().Be("ux_ledger_entries_correlation_category");
    }

    [Fact]
    public async Task A_stored_resulting_balance_is_never_negative()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (_, opening) = await PickAccountAsync(db);

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update finance.ledger_entries set resulting_cash_minor = -1 where id = {0}",
            opening.Id);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_ledger_entries_balances");
    }

    private static async Task<(ClubAccount Account, LedgerEntry Opening)> PickAccountAsync(
        TouchlineManagerDbContext db)
    {
        var account = await db.ClubAccounts.OrderBy(candidate => candidate.ClubId).FirstAsync();

        var opening = await db.LedgerEntries.SingleAsync(entry =>
            entry.ClubId == account.ClubId && entry.Category == LedgerCategory.OpeningBalance);

        return (account, opening);
    }

    private static string? ConstraintOf(
        FluentAssertions.Specialized.ExceptionAssertions<DbUpdateException> exception) =>
        (exception.Which.InnerException as PostgresException)?.ConstraintName;
}

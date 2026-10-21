using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.Competition;

namespace Tactical11Manager.Infrastructure.Tests.Finance;

/// <summary>
/// The weekly finance settlement over a real seeded world (`CON-2`, `FIN-4`, `FIN-7`, `FIN-9`, `FIN-17`).
/// </summary>
/// <remarks>
/// The property no unit test can reach: a week that is settled and then retried must charge nothing more.
/// It is asserted by running the settlement twice against real rows and reading the ledger and the balances
/// back, because the guarantee is the ledger's unique index and the correlation key, not the run's memory.
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class WeeklyFinanceRunTests
{
    /// <summary>Initializes the tests.</summary>
    public WeeklyFinanceRunTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the run settles.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task Settling_a_week_charges_every_club_and_a_retry_charges_nothing_more()
    {
        var week = new DateOnly(2026, 9, 27);

        await using var scope = Fixture.CreateScope();
        var run = scope.ServiceProvider.GetRequiredService<RunWeeklyFinance>();

        var first = await run.ExecuteAsync(week, CancellationToken.None);

        first.Clubs.Should().BeGreaterThan(0);
        first.Skipped.Should().Be(0);
        first.Posted.Should().BeGreaterThan(0);

        await using var readScope = Fixture.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var entriesAfterFirst = await readDb.LedgerEntries
            .CountAsync(entry => entry.CorrelationId.StartsWith(WeekPrefix(week)));

        entriesAfterFirst.Should().BeGreaterThanOrEqualTo(
            first.Clubs * 2,
            "every club is credited its sponsorship and charged at least its wages (FIN-4, FIN-7)");

        var balancesAfterFirst = await readDb.ClubAccounts
            .ToDictionaryAsync(
                account => account.ClubId,
                account => new { account.CashMinor, account.ReservedMinor, account.LastLedgerSequence });

        var second = await run.ExecuteAsync(week, CancellationToken.None);

        second.Clubs.Should().Be(first.Clubs);
        second.Skipped.Should().Be(first.Clubs, "the week is already settled");
        second.Posted.Should().Be(0);

        await using var verifyScope = Fixture.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await verifyDb.LedgerEntries
                .CountAsync(entry => entry.CorrelationId.StartsWith(WeekPrefix(week))))
            .Should()
            .Be(entriesAfterFirst, "a retried week writes no new entry (FIN-17)");

        var balancesAfterSecond = await verifyDb.ClubAccounts
            .ToDictionaryAsync(
                account => account.ClubId,
                account => new { account.CashMinor, account.ReservedMinor, account.LastLedgerSequence });

        balancesAfterSecond.Should().BeEquivalentTo(balancesAfterFirst, "a retried week moves no money");
    }

    [Fact]
    public async Task Settling_a_week_books_every_clubs_merchandise_and_upkeep_once_at_level_one()
    {
        var week = new DateOnly(2026, 10, 4);

        await using var scope = Fixture.CreateScope();
        var run = scope.ServiceProvider.GetRequiredService<RunWeeklyFinance>();

        var first = await run.ExecuteAsync(week, CancellationToken.None);
        var second = await run.ExecuteAsync(week, CancellationToken.None);

        second.Skipped.Should().Be(first.Clubs, "the week is already settled");

        await using var readScope = Fixture.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var weekly = await readDb.LedgerEntries
            .Where(entry => entry.CorrelationId.StartsWith(WeekPrefix(week)))
            .ToListAsync();

        weekly.Count(entry => entry.Category == LedgerCategory.Merchandise).Should().Be(first.Clubs, "every club has fans (STRC-6)");
        weekly.Count(entry => entry.Category == LedgerCategory.StructureUpkeep).Should().Be(first.Clubs, "one combined entry per club (STRC-8)");
        weekly.Where(entry => entry.Category == LedgerCategory.Merchandise).Should().OnlyContain(entry => entry.CashDeltaMinor > 0);
        weekly.Where(entry => entry.Category == LedgerCategory.StructureUpkeep).Should().OnlyContain(entry => entry.CashDeltaMinor < 0);

        var rows = await readDb.ClubStructureTakings.Where(row => row.WeekOf == week).ToListAsync();

        rows.Count(row => row.RowKind == StructureTakingsKind.WeekSale).Should().Be(first.Clubs);
        rows.Count(row => row.RowKind == StructureTakingsKind.Upkeep).Should().Be(first.Clubs * 2, "one row per building");
        rows.Should().OnlyContain(row => row.Level == StructureRuleSet.OpeningLevel, "a new world opens both buildings at level 1");

        // Each club's two upkeep rows add up to the one ledger entry that charged them.
        foreach (var upkeep in weekly.Where(entry => entry.Category == LedgerCategory.StructureUpkeep))
        {
            rows.Where(row => row.LedgerEntryId == upkeep.Id).Sum(row => row.AmountMinor).Should().Be(upkeep.CashDeltaMinor);
        }
    }

    private static string WeekPrefix(DateOnly week) => $"weekly-run:{week:yyyy-MM-dd}:";
}

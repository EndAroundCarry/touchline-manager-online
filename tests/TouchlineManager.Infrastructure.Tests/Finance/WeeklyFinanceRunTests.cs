using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Finance;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.Competition;

namespace TouchlineManager.Infrastructure.Tests.Finance;

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
        var readDb = readScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

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
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

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

    private static string WeekPrefix(DateOnly week) => $"weekly-run:{week:yyyy-MM-dd}:";
}

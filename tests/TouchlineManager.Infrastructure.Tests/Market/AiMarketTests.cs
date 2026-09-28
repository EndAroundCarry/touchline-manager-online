using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Market;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Market;

/// <summary>
/// The AI transfer market over a real seeded world (`TRF-12`, `INS-12`).
/// </summary>
/// <remarks>
/// The policy and the orchestration are pinned by the unit tests. What this suite proves is the property
/// Stage 10 asks for in the presence of real money and real squads: the AI lists its surplus and bids within
/// its budget, through the same ledger and squad rules a human obeys, with no club driven illegal, no
/// balance driven negative, and the ledger still replaying to the stored balances.
/// <para>
/// Its own world per test, started in <see cref="InitializeAsync"/>, rather than a shared collection fixture:
/// every test here mutates the market and the clock, so one test's transfers would decide the next test's
/// outcome. A fresh seeded world per test is the honest cost of an independent assertion.
/// </para>
/// </remarks>
public sealed class AiMarketTests : IAsyncLifetime, IDisposable
{
    private readonly AiMarketFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task The_AI_lists_its_surplus_then_bids_on_the_market_within_its_budget()
    {
        var firstDay = await EvaluateAsync();
        var secondDay = await AdvanceAndEvaluateAsync();

        firstDay.Listed.Should().Be(firstDay.Clubs, "every seeded club is one player above its target");
        firstDay.Bids.Should().Be(0, "the world begins with supply; demand weighs a listing that already exists");
        firstDay.Skipped.Should().Be(0);
        secondDay.Bids.Should().BeGreaterThan(0, "the AI now has listings to weigh and buys where it improves (TRF-12)");

        await AssertMarketIsSoundAsync();
    }

    [Fact]
    public async Task A_retried_evaluation_replays_rather_than_duplicating()
    {
        var first = await EvaluateAsync();

        await using var scope = _fixture.CreateScope();
        var before = await CountsAsync(scope);

        // Same day, same deterministic keys: the pass must replay rather than list or bid a second time.
        var again = await EvaluateAsync();

        again.Listed.Should().Be(0);
        again.Bids.Should().Be(0);
        again.Skipped.Should().Be(0);

        var after = await CountsAsync(scope);

        after.Listings.Should().Be(before.Listings);
        after.Bids.Should().Be(before.Bids);
        after.Decisions.Should().Be(before.Decisions);
        after.Reserved.Should().Be(before.Reserved, "a replay reserves nothing twice (FIN-10)");

        first.Listed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Resolving_the_market_moves_money_and_keeps_every_club_legal()
    {
        await EvaluateAsync();
        await AdvanceAndEvaluateAsync();

        var resolved = await ResolveSomeListingsAsync();

        resolved.Should().BeGreaterThan(0, "the AI's bids must be able to settle (TRF-10)");

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        (await db.TransferOutcomes.CountAsync()).Should().BeGreaterThan(0);

        await AssertMarketIsSoundAsync();
    }

    /// <summary>Runs one evaluation pass at the current game instant.</summary>
    private async Task<EvaluateAiMarketResult> EvaluateAsync()
    {
        await using var scope = _fixture.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<EvaluateAiMarket>()
            .ExecuteAsync(CancellationToken.None);
    }

    /// <summary>Moves the clock a day and evaluates, so decisions that depend on the first pass can be made.</summary>
    private async Task<EvaluateAiMarketResult> AdvanceAndEvaluateAsync()
    {
        _fixture.Clock.Advance(TimeSpan.FromDays(1));

        return await EvaluateAsync();
    }

    /// <summary>Resolves a bounded handful of listings that hold a leading bid.</summary>
    private async Task<int> ResolveSomeListingsAsync()
    {
        List<Guid> listingIds;

        await using (var readScope = _fixture.CreateScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            listingIds = await db.TransferBids
                .Where(bid => bid.Status == BidStatus.Leading)
                .OrderBy(bid => bid.ListingId)
                .Select(bid => bid.ListingId)
                .Distinct()
                .Take(10)
                .ToListAsync();
        }

        var resolved = 0;

        foreach (var listingId in listingIds)
        {
            await using var resolveScope = _fixture.CreateScope();

            await resolveScope.ServiceProvider.GetRequiredService<ResolveListing>()
                .ExecuteAsync(listingId, CancellationToken.None);

            resolved++;
        }

        return resolved;
    }

    /// <summary>
    /// The properties the exit criterion names: no club driven illegal, no balance driven negative, and the
    /// ledger still replaying to the balances it is the source of (`FIN-18`, `SQ-2`).
    /// </summary>
    private async Task AssertMarketIsSoundAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var squads = await (
            from contract in db.PlayerContracts
            where contract.Status == ContractStatus.Active
            join player in db.Players on contract.PlayerId equals player.Id
            group player.PrimaryPosition by contract.ClubId into club
            select new { ClubId = club.Key, Count = club.Count(), Goalkeepers = club.Count(p => p == PlayerPosition.Goalkeeper) })
            .ToListAsync();

        squads.Should().NotContain(club => club.Count < WorldRuleSet.SquadMinimumRegistered);
        squads.Should().NotContain(club => club.Goalkeepers < WorldRuleSet.MinimumGoalkeepers);

        var accounts = await db.ClubAccounts
            .Select(account => new { account.ClubId, account.CashMinor, account.ReservedMinor })
            .ToListAsync();

        accounts.Should().NotContain(account => account.CashMinor < 0);
        accounts.Should().NotContain(account => account.ReservedMinor < 0);
        accounts.Should().NotContain(account => account.ReservedMinor > account.CashMinor);

        var ledger = await db.LedgerEntries
            .GroupBy(entry => entry.ClubId)
            .Select(group => new
            {
                ClubId = group.Key,
                Cash = group.Sum(entry => entry.CashDeltaMinor),
                Reserved = group.Sum(entry => entry.ReservedDeltaMinor),
            })
            .ToListAsync();

        foreach (var account in accounts)
        {
            var replayed = ledger.Single(row => row.ClubId == account.ClubId);

            replayed.Cash.Should().Be(account.CashMinor, "FIN-18: the ledger is the balance");
            replayed.Reserved.Should().Be(account.ReservedMinor, "FIN-18: the ledger is the balance");
        }

        var decisions = await db.AiMarketDecisions.ToListAsync();

        decisions.Should().NotBeEmpty();
        decisions.Should().OnlyContain(decision => decision.PolicyVersion == AiMarketPolicyVersions.Version);
        decisions.Should().OnlyContain(decision => decision.InputsHash.Length == 64);
        decisions.Should().OnlyContain(decision =>
            decision.Action == AiMarketAction.Listed
                ? decision.ListingId != null && decision.BidId == null
                : decision.BidId != null && decision.ListingId == null);
    }

    private static async Task<(int Listings, int Bids, int Decisions, long Reserved)> CountsAsync(
        AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return (
            await db.TransferListings.CountAsync(),
            await db.TransferBids.CountAsync(),
            await db.AiMarketDecisions.CountAsync(),
            await db.ClubAccounts.SumAsync(account => account.ReservedMinor));
    }
}

/// <summary>The AI market tests' own seeded world, one per test.</summary>
public sealed class AiMarketFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public AiMarketFixture()
        : base("touchline_ai_market")
    {
    }
}

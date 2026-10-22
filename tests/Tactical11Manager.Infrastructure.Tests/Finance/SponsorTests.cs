using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Calendar;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Calendar;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.World;

namespace Tactical11Manager.Infrastructure.Tests.Finance;

/// <summary>
/// The season's sponsors against real PostgreSQL 17: the offers a seeded world makes, the computer clubs' deals, the board's signing at the
/// deadline, a manager's signing with the matchdays already played paid at once, and the deadline on the calendar (`SPN-1`…`SPN-6`).
/// </summary>
/// <remarks>Its own world per test, because the tests delete deals and publish matchdays.</remarks>
public sealed class SponsorTests : IAsyncLifetime, IDisposable
{
    private readonly SponsorFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_seeded_world_makes_every_club_three_offers_and_the_computer_clubs_sign_at_once()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var clubs = await db.Clubs.CountAsync();
        var offers = await db.SponsorOffers.ToListAsync();
        var deals = await db.SponsorDeals.ToListAsync();

        clubs.Should().BeGreaterThan(0);
        offers.Should().HaveCount(clubs * 3);
        offers.GroupBy(offer => offer.ClubId).Should().OnlyContain(group => group.Select(offer => offer.Kind).Distinct().Count() == 3);
        offers.Should().OnlyContain(offer => offer.SponsorName.Length > 0 && offer.FixedSeasonMinor > 0);

        deals.Should().HaveCount(clubs, "no manager holds a club of a new world, so every club's deal is the computer's");
        deals.Should().OnlyContain(deal => deal.SignedBy == SponsorSigner.Ai && deal.FixedMatchdayMinor > 0);
        deals.Should().OnlyContain(deal => offers.Any(offer => offer.Id == deal.OfferId && offer.ClubId == deal.ClubId));
    }

    [Fact]
    public async Task At_the_deadline_the_board_signs_the_balanced_offer_for_every_club_that_has_not_signed()
    {
        var (divisionSeasonId, unsigned) = await RemoveDealsAsync(3);

        await using (var scope = _fixture.CreateScope())
        {
            var queries = scope.ServiceProvider.GetRequiredService<ISponsorQueries>();

            (await queries.ListClubsWithoutDealAsync(divisionSeasonId, CancellationToken.None)).Should().BeEquivalentTo(unsigned);
        }

        await using (var scope = _fixture.CreateScope())
        {
            var signed = await scope.ServiceProvider.GetRequiredService<SignDueSponsorDeals>()
                .ExecuteAsync(divisionSeasonId, CancellationToken.None);

            signed.Should().Be(3);
        }

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var deals = await db.SponsorDeals.Where(deal => unsigned.Contains(deal.ClubId)).ToListAsync();

            deals.Should().HaveCount(3);
            deals.Should().OnlyContain(deal => deal.SignedBy == SponsorSigner.Board && deal.Kind == SponsorOfferKind.Balanced);
            deals.Should().OnlyContain(deal => deal.Objective != 0);

            var accounts = await db.ClubAccounts.Where(account => unsigned.Contains(account.ClubId)).ToListAsync();

            accounts.Should().OnlyContain(account => account.OverdraftLimitMinor > 0, "the limit is set from the forecast at signing (BUD-3)");

            // Nothing is left for a second pass to do.
            await using var again = _fixture.CreateScope();

            (await again.ServiceProvider.GetRequiredService<SignDueSponsorDeals>().ExecuteAsync(divisionSeasonId, CancellationToken.None))
                .Should().Be(0);
        }
    }

    [Fact]
    public async Task A_signing_after_two_published_matchdays_pays_both_at_once_and_keeps_the_balance_a_replay()
    {
        var (divisionSeasonId, clubs) = await RemoveDealsAsync(1);
        var clubId = clubs[0];

        await PublishRoundsAsync(divisionSeasonId, 2);

        SponsorSigningResult result;

        await using (var scope = _fixture.CreateScope())
        {
            var signing = scope.ServiceProvider.GetRequiredService<SponsorSigning>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await using var transaction = await unitOfWork.BeginTransactionAsync(TransactionIsolation.ReadCommitted, CancellationToken.None);

            result = await signing.SignAsync(
                clubId,
                SponsorOfferKind.Performance,
                SeasonObjective.TopThree,
                expectedVersion: 0,
                new SponsorActor(SponsorSigner.Manager, Guid.CreateVersion7(), "sponsor-test", "hash"),
                _fixture.Clock.UtcNow,
                CancellationToken.None);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        result.Outcome.Should().Be(SponsorSigningOutcome.Agreed);
        result.BackPaidMatchdays.Should().Be(2);

        await using var readScope = _fixture.CreateScope();
        var db = readScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var deal = await db.SponsorDeals.SingleAsync(candidate => candidate.ClubId == clubId);
        var entry = await db.LedgerEntries.SingleAsync(candidate => candidate.ClubId == clubId && candidate.Category == LedgerCategory.SponsorMatchday);
        var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);

        deal.Kind.Should().Be(SponsorOfferKind.Performance);
        deal.Objective.Should().Be(SeasonObjective.TopThree);
        deal.SignedBy.Should().Be(SponsorSigner.Manager);
        deal.Version.Should().Be(1);
        entry.CashDeltaMinor.Should().Be(deal.FixedMatchdayMinor * 2);
        entry.CorrelationId.Should().Be($"sponsor-signing:{deal.Id:N}");
        account.OverdraftLimitMinor.Should().BeGreaterThan(0);

        (await db.LedgerEntries.Where(candidate => candidate.ClubId == clubId).SumAsync(candidate => candidate.CashDeltaMinor))
            .Should().Be(account.CashMinor, "the ledger replays to the balance (FIN-18)");

        var context = await readScope.ServiceProvider.GetRequiredService<ISponsorQueries>().GetClubContextAsync(clubId, CancellationToken.None);

        context.Should().NotBeNull();
        context!.PublishedMatchdays.Should().Be(2);
        context.TierNumber.Should().Be(1);
        context.StandingPoints.Should().BeInRange(1, 100);
    }

    [Fact]
    public async Task A_second_signing_of_the_same_deal_with_the_old_version_is_refused()
    {
        var (_, clubs) = await RemoveDealsAsync(1);
        var clubId = clubs[0];

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = _fixture.CreateScope();
            var signing = scope.ServiceProvider.GetRequiredService<SponsorSigning>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await using var transaction = await unitOfWork.BeginTransactionAsync(TransactionIsolation.ReadCommitted, CancellationToken.None);

            var result = await signing.SignAsync(
                clubId,
                SponsorOfferKind.Steady,
                SeasonObjective.StayUp,
                expectedVersion: 0,
                new SponsorActor(SponsorSigner.Manager, Guid.CreateVersion7(), "sponsor-test", "hash"),
                _fixture.Clock.UtcNow,
                CancellationToken.None);

            if (attempt == 0)
            {
                result.Outcome.Should().Be(SponsorSigningOutcome.Agreed);
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
            }
            else
            {
                result.Outcome.Should().Be(SponsorSigningOutcome.PreconditionFailed, "the first signing moved the version on");
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        await using var readScope = _fixture.CreateScope();

        (await readScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>().SponsorDeals.CountAsync(deal => deal.ClubId == clubId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Matchdays_published_after_an_instant_are_counted_for_a_new_managers_window()
    {
        var (divisionSeasonId, _) = await RemoveDealsAsync(0);

        await PublishRoundsAsync(divisionSeasonId, 2);

        await using var scope = _fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ISponsorQueries>();
        var now = _fixture.Clock.UtcNow;

        (await queries.CountMatchdaysPublishedSinceAsync(divisionSeasonId, now.AddDays(-1), CancellationToken.None)).Should().Be(2);
        (await queries.CountMatchdaysPublishedSinceAsync(divisionSeasonId, now.AddDays(1), CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task The_calendar_shows_the_deadline_to_a_club_that_has_not_signed_and_not_to_one_that_has()
    {
        var (_, clubs) = await RemoveDealsAsync(1);
        var unsignedClub = clubs[0];

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var signedClub = await db.SponsorDeals.Where(deal => deal.ClubId != unsignedClub).Select(deal => deal.ClubId).FirstAsync();

        var context = await scope.ServiceProvider.GetRequiredService<ISponsorQueries>().GetClubContextAsync(unsignedClub, CancellationToken.None);
        var deadline = SponsorDeadline.LockAtFor(context!.FirstMatchday);
        var window = new CalendarWindow(deadline.AddDays(-10), deadline.AddDays(10), deadline.AddDays(-9));
        var source = scope.ServiceProvider.GetServices<ICalendarEventSource>().OfType<SponsorCalendarSource>().Single();

        var events = await source.GetEventsAsync(unsignedClub, window, CancellationToken.None);

        events.Should().ContainSingle().Which.At.Should().Be(deadline);
        (await source.GetEventsAsync(signedClub, window, CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>Deletes some clubs' deals from the first division, as if their managers had not signed, and returns the division and the clubs.</summary>
    private async Task<(Guid DivisionSeasonId, List<Guid> Clubs)> RemoveDealsAsync(int count)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var divisionSeasonId = await db.DivisionSeasons.OrderBy(candidate => candidate.Id).Select(candidate => candidate.Id).FirstAsync();
        var clubs = (await db.ClubSeasonEntries
                .Where(entry => entry.DivisionSeasonId == divisionSeasonId)
                .Select(entry => entry.ClubId)
                .ToListAsync())
            .Order()
            .Take(count)
            .ToList();

        await db.SponsorDeals.Where(deal => clubs.Contains(deal.ClubId)).ExecuteDeleteAsync();

        return (divisionSeasonId, clubs);
    }

    /// <summary>Publishes the first rounds of a division-season without playing them, so the count of played matchdays moves.</summary>
    private async Task PublishRoundsAsync(Guid divisionSeasonId, int rounds)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var matchdays = await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId)
            .OrderBy(matchday => matchday.RoundNumber)
            .Take(rounds)
            .ToListAsync();

        foreach (var matchday in matchdays)
        {
            matchday.MarkStaged(now);
            matchday.Publish(now);
        }

        await db.SaveChangesAsync();
    }
}

/// <summary>The sponsor tests' own seeded world, one per test.</summary>
public sealed class SponsorFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public SponsorFixture()
        : base("tactical11_sponsor")
    {
    }
}

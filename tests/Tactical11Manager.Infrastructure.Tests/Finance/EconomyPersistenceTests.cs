using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.World;

namespace Tactical11Manager.Infrastructure.Tests.Finance;

/// <summary>
/// What the economy rework's schema guarantees over a real seeded world: the cash may go below zero only to the account floor, reserved funds
/// never outlive the cash above zero, a ledger in the red still replays, a club signs one sponsor deal a season, and its standing moves once a
/// season (`DEBT-1`, `FIN-13`, `FIN-18`, `SPN-1`, `SPN-2`, `STD-3`).
/// </summary>
/// <remarks>
/// Every test that writes does it inside a transaction it never commits, so the shared world is as it was for the next test.
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class EconomyPersistenceTests
{
    private readonly WorldFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public EconomyPersistenceTests(WorldFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_cash_may_fall_to_minus_the_floor_and_no_further()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await FreeAccountAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();

        await db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set overdraft_limit_minor = 500, overdraft_floor_minor = 1000, cash_minor = -1000 where id = {0}",
            account.Id);

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set cash_minor = -1001 where id = {0}",
            account.Id);

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_club_accounts_cash_floor");
    }

    [Fact]
    public async Task Without_an_overdraft_the_cash_is_never_below_zero()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await FreeAccountAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set cash_minor = -1 where id = {0}",
            account.Id);

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_club_accounts_cash_floor");
    }

    [Fact]
    public async Task Reserved_funds_never_outlive_the_cash_above_zero()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await FreeAccountAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();

        await db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set overdraft_limit_minor = 500, overdraft_floor_minor = 1000, cash_minor = -10 where id = {0}",
            account.Id);

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set reserved_minor = 1 where id = {0}",
            account.Id);

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_club_accounts_reserved_within_cash");
    }

    [Fact]
    public async Task The_floor_is_never_shallower_than_the_limit()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await FreeAccountAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var shallow = () => db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set overdraft_limit_minor = 500, overdraft_floor_minor = 100 where id = {0}",
            account.Id);

        (await shallow.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_club_accounts_overdraft");
    }

    [Fact]
    public async Task An_unknown_financial_state_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await FreeAccountAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update finance.club_accounts set financial_state = 'bankrupt' where id = {0}",
            account.Id);

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_club_accounts_financial_state");
    }

    [Fact]
    public async Task A_stored_resulting_reserve_is_never_negative_or_beyond_the_cash_above_zero()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var opening = await db.LedgerEntries.OrderBy(entry => entry.ClubId).FirstAsync(entry => entry.Category == LedgerCategory.OpeningBalance);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var beyond = () => db.Database.ExecuteSqlRawAsync(
            "update finance.ledger_entries set resulting_reserved_minor = resulting_cash_minor + 1 where id = {0}",
            opening.Id);

        (await beyond.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_ledger_entries_balances");
    }

    [Fact]
    public async Task A_club_in_the_red_replays_from_its_ledger()
    {
        // FIN-18 with negative cash: the account is still the running total of its entries, and the entry states the negative balance.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await FreeAccountAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var now = _fixture.Clock.UtcNow;
        account.SetOverdraft(overdraftLimitMinor: 1_000_000, now);

        var entry = account.Post(
            new LedgerPosting(
                Guid.CreateVersion7(),
                LedgerCategory.Wages,
                -(account.CashMinor + 500_000),
                0,
                LedgerSourceType.WeeklyRun,
                SourceId: null,
                $"red-{Guid.NewGuid():N}",
                "finance.wages",
                "{}"),
            now);
        db.LedgerEntries.Add(entry);
        await db.SaveChangesAsync();

        account.CashMinor.Should().Be(-500_000);
        entry.ResultingCashMinor.Should().Be(-500_000);

        var deltas = await db.LedgerEntries.Where(candidate => candidate.ClubId == account.ClubId).Select(candidate => candidate.CashDeltaMinor).ToListAsync();

        deltas.Sum().Should().Be(account.CashMinor, "the ledger replays to the stored balance with the club in the red (FIN-18)");

        var stored = await db.Database
            .SqlQueryRaw<long>("select cash_minor as \"Value\" from finance.club_accounts where id = {0}", account.Id)
            .SingleAsync();

        stored.Should().Be(-500_000);
    }

    [Fact]
    public async Task Offers_and_a_deal_round_trip_and_a_club_signs_once_a_season()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var club = await db.Clubs.OrderBy(candidate => candidate.Id).FirstAsync();
        var season = await db.Seasons.OrderBy(candidate => candidate.SequenceNumber).FirstAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        // A seeded world already holds the club's offers and its computer deal; the test writes its own in their place, inside the transaction.
        await db.SponsorDeals.Where(candidate => candidate.ClubId == club.Id).ExecuteDeleteAsync();
        await db.SponsorOffers.Where(candidate => candidate.ClubId == club.Id).ExecuteDeleteAsync();
        var now = _fixture.Clock.UtcNow;

        var offers = SponsorOffers.For(_fixture.WorldId, season.SequenceNumber, club.Id, tier: 1, fans: 20_000, standing: club.Reputation)
            .Select(offer => ClubSponsorOffer.Record(Guid.CreateVersion7(), club.Id, season.Id, offer, 1, 20_000, club.Reputation, now))
            .ToList();
        db.SponsorOffers.AddRange(offers);
        await db.SaveChangesAsync();

        var chosen = offers.Single(offer => offer.Kind == SponsorOfferKind.Balanced);
        var quote = SponsorOffers.Quotes(chosen.Kind, chosen.Tier, chosen.Fans, chosen.StandingPoints).Single(candidate => candidate.Objective == SeasonObjective.TopSix);
        var deal = ClubSponsorDeal.Sign(Guid.CreateVersion7(), chosen, SeasonObjective.TopSix, quote.MetMinor, quote.ExceededMinor, SponsorSigner.Manager, now);
        db.SponsorDeals.Add(deal);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var offersRead = await db.SponsorOffers.AsNoTracking().Where(candidate => candidate.ClubId == club.Id).ToListAsync();
        var dealRead = await db.SponsorDeals.AsNoTracking().SingleAsync(candidate => candidate.ClubId == club.Id);

        offersRead.Should().HaveCount(3).And.OnlyContain(candidate => candidate.SponsorName.Length > 0);
        offersRead.Select(candidate => candidate.Kind).Should().BeEquivalentTo(Enum.GetValues<SponsorOfferKind>());
        dealRead.Objective.Should().Be(SeasonObjective.TopSix);
        dealRead.TargetPlace.Should().Be(6);
        dealRead.Kind.Should().Be(SponsorOfferKind.Balanced);
        dealRead.SignedBy.Should().Be(SponsorSigner.Manager);
        dealRead.FixedMatchdayMinor.Should().Be(deal.FixedMatchdayMinor);
        dealRead.BonusMetMinor.Should().Be(quote.MetMinor);
        dealRead.Outcome.Should().BeNull();

        var duplicateOffer = ClubSponsorOffer.Record(
            Guid.CreateVersion7(),
            club.Id,
            season.Id,
            new SponsorOffer(SponsorOfferKind.Steady, "Another Name", 1, 5_000),
            1,
            20_000,
            club.Reputation,
            now);
        db.SponsorOffers.Add(duplicateOffer);

        var again = async () => await db.SaveChangesAsync();

        (await again.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ux_sponsor_offers_club_season_kind");
    }

    [Fact]
    public async Task A_second_deal_for_the_same_club_and_season_is_refused_and_a_settled_deal_carries_its_whole_outcome()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var club = await db.Clubs.OrderBy(candidate => candidate.Id).FirstAsync();
        var season = await db.Seasons.OrderBy(candidate => candidate.SequenceNumber).FirstAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        // A seeded world already holds the club's offers and its computer deal; the test writes its own in their place, inside the transaction.
        await db.SponsorDeals.Where(candidate => candidate.ClubId == club.Id).ExecuteDeleteAsync();
        await db.SponsorOffers.Where(candidate => candidate.ClubId == club.Id).ExecuteDeleteAsync();
        var now = _fixture.Clock.UtcNow;

        var offer = ClubSponsorOffer.Record(
            Guid.CreateVersion7(), club.Id, season.Id, new SponsorOffer(SponsorOfferKind.Steady, "Northgate Foods", 3_400_000, 5_000), 1, 20_000, 50, now);
        db.SponsorOffers.Add(offer);

        var deal = ClubSponsorDeal.Sign(Guid.CreateVersion7(), offer, SeasonObjective.StayUp, 100, 150, SponsorSigner.Board, now);
        db.SponsorDeals.Add(deal);
        await db.SaveChangesAsync();

        var halfSettled = () => db.Database.ExecuteSqlRawAsync(
            "update finance.sponsor_deals set outcome = 'met' where id = {0}",
            deal.Id);

        (await halfSettled.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_sponsor_deals_settled_shape");
    }

    [Fact]
    public async Task A_club_standing_moves_once_a_season_and_stays_on_its_scale()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var club = await db.Clubs.OrderBy(candidate => candidate.Id).FirstAsync();
        var season = await db.Seasons.OrderBy(candidate => candidate.SequenceNumber).FirstAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = _fixture.Clock.UtcNow;

        club.StandingCentis.Should().Be(club.Reputation * ClubStanding.CentisPerPoint, "a seeded club opens at the whole points of its tier");

        var after = ClubStanding.Step(club.StandingCentis, 1, 2, ObjectiveOutcome.Met, 1);
        db.ClubStandingEvents.Add(ClubStandingEvent.Record(Guid.CreateVersion7(), club.Id, season.Id, 1, 1, 2, ObjectiveOutcome.Met, club.StandingCentis, after, now));
        await db.SaveChangesAsync();

        db.ClubStandingEvents.Add(ClubStandingEvent.Record(Guid.CreateVersion7(), club.Id, season.Id, 1, 1, 3, null, after, after, now));

        var twice = async () => await db.SaveChangesAsync();

        (await twice.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ux_club_standing_events_club_season");
    }

    [Fact]
    public async Task A_standing_off_the_scale_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var club = await db.Clubs.OrderBy(candidate => candidate.Id).FirstAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update world.clubs set standing_centis = 10001 where id = {0}",
            club.Id);

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_clubs_standing");
    }

    [Fact]
    public async Task Every_seeded_club_opens_with_wages_it_can_carry()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var tiers = await (
            from entry in db.ClubSeasonEntries
            join divisionSeason in db.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in db.Divisions on divisionSeason.DivisionId equals division.Id
            select new { entry.ClubId, division.TierNumber })
            .ToListAsync();

        var wages = await db.PlayerContracts
            .Where(contract => contract.Status == ContractStatus.Active)
            .GroupBy(contract => contract.ClubId)
            .Select(group => new { ClubId = group.Key, Bill = group.Sum(contract => contract.SeasonWageMinor) })
            .ToDictionaryAsync(row => row.ClubId, row => row.Bill);

        tiers.Should().NotBeEmpty();

        foreach (var club in tiers)
        {
            // The squads are fair templates (plan cheeky-dongarra), not sized into the 70-92% band, so their bills follow their ratings and sit
            // below it; what is still owed is that no club opens with a squad it cannot carry (FIN-2).
            var forecast = EconomyRuleSet.FreshClubForecastMinor(club.TierNumber);

            wages[club.ClubId].Should().BeLessThanOrEqualTo(
                forecast * EconomyRuleSet.SeededWageBandHighBp / 10_000,
                "no club opens with a squad it cannot carry (FIN-2)");
        }
    }

    /// <summary>
    /// An account with nothing reserved whose row agrees with its ledger. Other suites in the collection play matchdays in the shared world, so
    /// an account is picked by what it holds and not by its place in the list.
    /// </summary>
    private static async Task<ClubAccount> FreeAccountAsync(Tactical11ManagerDbContext db)
    {
        var tails = await db.LedgerEntries
            .GroupBy(entry => entry.ClubId)
            .Select(group => new { ClubId = group.Key, Last = group.Max(entry => entry.Sequence), Cash = group.Sum(entry => entry.CashDeltaMinor) })
            .ToDictionaryAsync(row => row.ClubId);

        var candidates = await db.ClubAccounts
            .Where(candidate => candidate.ReservedMinor == 0 && candidate.CashMinor > 0)
            .OrderBy(candidate => candidate.ClubId)
            .ToListAsync();

        return candidates.First(candidate =>
            tails.TryGetValue(candidate.ClubId, out var tail) && tail.Last == candidate.LastLedgerSequence && tail.Cash == candidate.CashMinor);
    }
}

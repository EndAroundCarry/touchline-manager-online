using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.World;

/// <summary>
/// A takeover with a clean start against real PostgreSQL 17: the old players go to the free agents, the club returns to its tier's opening
/// squad, ground, buildings and cash with no debt, and a fixture already locked keeps the players it was locked with (`TKO-1`…`TKO-4`, `SQ-7`).
/// </summary>
/// <remarks>
/// Its own world per test, because the tests put clubs into debt, grow grounds and play rounds, and a shared world would let one test's club
/// decide another's outcome.
/// </remarks>
public sealed class CleanStartTakeoverTests : WorldTestBase, IAsyncLifetime, IDisposable
{
    // The fixture clock reads Wednesday 23 September 2026 at noon, so this week opened on Monday the 21st.
    private static readonly DateOnly ThisWeek = new(2026, 9, 21);

    private readonly CleanStartFixture _fixture;

    /// <summary>Initializes the tests with a world of their own.</summary>
    public CleanStartTakeoverTests()
        : this(new CleanStartFixture())
    {
    }

    private CleanStartTakeoverTests(CleanStartFixture fixture)
        : base(fixture) => _fixture = fixture;

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_clean_start_returns_a_club_in_administration_to_its_tiers_opening_state()
    {
        var (clubId, bidderClubId, _) = await ClubsAsync();

        await RunAiClubsAsync();
        await GrowTheClubAsync(clubId);

        var listingId = await ListAPlayerWithABidAsync(clubId, bidderClubId);

        await PutInAdministrationAsync(clubId);

        var oldPlayerIds = await ActivePlayerIdsAsync(clubId);
        var oldWageBill = await WageBillAsync(clubId);
        var bidderReservedBefore = await ReservedAsync(bidderClubId);
        var userId = await NewManagerAsync();

        oldPlayerIds.Should().HaveCountGreaterThanOrEqualTo(WorldRuleSet.SquadMinimumRegistered);
        bidderReservedBefore.Should().Be(1_000_000, "the other club's leading bid is reserved");

        var claimed = await ClaimAsync(userId, clubId, TakeoverStarts.CleanStartCode);

        claimed.Outcome.Should().Be(ClaimClubOutcome.Claimed);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var tenure = await db.ClubTenures.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId && candidate.EndedAt == null);

        // The old players were released without a payout and are free agents.
        var oldContracts = await db.PlayerContracts.AsNoTracking().Where(contract => oldPlayerIds.Contains(contract.PlayerId)).ToListAsync();

        oldContracts.Should().OnlyContain(contract => contract.Status == ContractStatus.Closed && contract.ClosedReason == PlayerContractCloseReasons.Released);
        (await db.Players.AsNoTracking().Where(player => oldPlayerIds.Contains(player.Id)).ToListAsync())
            .Should().OnlyContain(player => player.Status == PlayerStatus.FreeAgent);
        (await db.PlayerRegistrations.AsNoTracking().Where(registration => oldPlayerIds.Contains(registration.PlayerId) && registration.Status == RegistrationStatus.Active).CountAsync())
            .Should().Be(0);
        (await db.LedgerEntries.AsNoTracking().CountAsync(entry => entry.ClubId == clubId && entry.Category == LedgerCategory.ReleasePayout))
            .Should().Be(0, "a clean start pays nobody off");

        // The new squad is the tier's default, with wages a fresh club can carry, and nobody from the old one is in it.
        var newPlayerIds = await ActivePlayerIdsAsync(clubId);
        var newWageBill = await WageBillAsync(clubId);
        var forecast = EconomyRuleSet.FreshClubForecastMinor(1);

        newPlayerIds.Should().HaveCountGreaterThanOrEqualTo(WorldRuleSet.SquadMinimumRegistered);
        newPlayerIds.Intersect(oldPlayerIds).Should().BeEmpty();

        // The squad is the tier's fair template, not sized into a band, so its bill follows its ratings; a fresh club can carry it.
        newWageBill.Should().BeInRange(1, forecast * EconomyRuleSet.CleanStartWageBandHighBp / 10_000);
        // Both squads are the tier's fair template, so their ratings and wages match but for the template's seeded nudges.
        newWageBill.Should().BeLessThanOrEqualTo(oldWageBill * 102 / 100, "a clean start is never dearer than the founding squad it replaces");
        (await db.PlayerRegistrations.AsNoTracking().CountAsync(registration => registration.ClubId == clubId && registration.Status == RegistrationStatus.Active))
            .Should().Be(newPlayerIds.Count);

        // The money: one entry sets the cash to the opening cash, the debt and the state are gone.
        var account = await db.ClubAccounts.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);
        var cleanStarts = await db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.ClubId == clubId && entry.Category == LedgerCategory.CleanStart)
            .ToListAsync();

        account.CashMinor.Should().Be(WorldRuleSet.OpeningCashMinorForTier(1));
        account.ReservedMinor.Should().Be(0);
        account.FinancialState.Should().Be(FinancialStateKind.Healthy);
        account.StateSinceMatchday.Should().Be(0);
        account.OverdraftLimitMinor.Should().Be(
            (await BasisAsync(clubId)).OverdraftLimitMinor,
            "a quarter of the forecast the fresh club has on the season's sponsor deal");
        account.OverdraftLimitMinor.Should().BePositive();
        cleanStarts.Should().ContainSingle();
        cleanStarts[0].CashDeltaMinor.Should().BePositive("the club was in debt");
        cleanStarts[0].SourceId.Should().Be(tenure.Id);
        cleanStarts[0].ResultingCashMinor.Should().Be(WorldRuleSet.OpeningCashMinorForTier(1));

        var lastEntry = await db.LedgerEntries.AsNoTracking().Where(entry => entry.ClubId == clubId).OrderByDescending(entry => entry.Sequence).FirstAsync();

        lastEntry.ResultingCashMinor.Should().Be(account.CashMinor, "the ledger replays to the balance (FIN-18)");

        // The ground and the buildings are back to level 1, and the upgrade under way is gone.
        var stadium = await db.ClubStadiums.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);
        var buildings = await db.ClubStructures.AsNoTracking().Where(candidate => candidate.ClubId == clubId).ToListAsync();

        stadium.Seats.Should().Be(EconomyRuleSet.OpeningSeats);
        buildings.Should().HaveCount(2).And.OnlyContain(building => building.Level == 1 && building.UpgradeTargetLevel == null && building.UpgradeDueAt == null);

        // Standing and fans are the tier's defaults.
        var club = await db.Clubs.AsNoTracking().SingleAsync(candidate => candidate.Id == clubId);
        var fanBase = await db.ClubFanBases.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);

        club.StandingCentis.Should().Be(ClubStanding.OpeningCentis(1));
        fanBase.Fans.Should().Be(FanRuleSet.OpeningFor(1).Fans);
        fanBase.SeasonTargetRank.Should().BeInRange(1, 18, "the target is worked out from the new squad");

        // The listing and the other club's reservation behind it are gone.
        (await db.TransferListings.AsNoTracking().SingleAsync(candidate => candidate.Id == listingId)).Status.Should().Be(ListingStatus.Cancelled);
        (await db.TransferBids.AsNoTracking().SingleAsync(candidate => candidate.ListingId == listingId)).Status.Should().Be(BidStatus.Released);
        (await db.ClubAccounts.AsNoTracking().SingleAsync(candidate => candidate.ClubId == bidderClubId)).ReservedMinor.Should().Be(0);

        // The default plan names only the new squad, and the draft team sheet that named the old one is gone.
        var plan = await db.TacticalPlans.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId && candidate.IsDefault);
        var assigned = await db.TacticalSlots.AsNoTracking()
            .Where(slot => slot.PlanId == plan.Id && slot.AssignedPlayerId != null)
            .Select(slot => slot.AssignedPlayerId!.Value)
            .ToListAsync();

        assigned.Should().HaveCount(11).And.OnlyContain(id => newPlayerIds.Contains(id));
        (await db.TacticalBenchSlots.AsNoTracking().CountAsync(place => place.PlanId == plan.Id)).Should().Be(0);
        (await db.FixtureTeamSheets.AsNoTracking().CountAsync(sheet => sheet.ClubId == clubId && sheet.Status == TeamSheetStatus.Draft)).Should().Be(0);

        // The audit log records the exception to STAD-6.
        (await db.AuditEntries.AsNoTracking().CountAsync(entry => entry.TargetId == tenure.Id && entry.Action == "world.club_tenure.clean_started"))
            .Should().Be(1);
    }

    [Fact]
    public async Task The_released_players_join_the_next_free_agent_batch_at_their_real_value()
    {
        var (clubId, _, _) = await ClubsAsync();
        var oldPlayerIds = await ActivePlayerIdsAsync(clubId);
        var userId = await NewManagerAsync();

        (await ClaimAsync(userId, clubId, TakeoverStarts.CleanStartCode)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        RunFreeAgentBatchResult batch;

        await using (var scope = _fixture.CreateScope())
        {
            batch = await scope.ServiceProvider.GetRequiredService<RunFreeAgentBatch>().ExecuteAsync(ThisWeek, CancellationToken.None);
        }

        batch.Released.Should().Be(oldPlayerIds.Count);

        await using var read = _fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var listings = await db.TransferListings.AsNoTracking()
            .Where(listing => oldPlayerIds.Contains(listing.PlayerId) && listing.FreeAgentBatchId != null)
            .ToListAsync();

        listings.Should().HaveCount(oldPlayerIds.Count);
        listings.Should().OnlyContain(listing => listing.SellerClubId == null && listing.Status == ListingStatus.Open);

        // One of them is priced from his own ability and age, with nothing hidden in it.
        var sample = listings[0];
        var attributes = await db.PlayerAttributes.AsNoTracking().SingleAsync(candidate => candidate.PlayerId == sample.PlayerId);
        var player = await db.Players.AsNoTracking().SingleAsync(candidate => candidate.Id == sample.PlayerId);
        var season = await db.Seasons.AsNoTracking().OrderByDescending(candidate => candidate.SequenceNumber).FirstAsync();
        var expected = FreeAgentTerms.CalculateReleasedFromTenths(attributes.ToSet().MeanTenths, season.GameYear - player.BirthGameYear);

        sample.MinimumFeeMinor.Should().Be(expected.MinimumFeeMinor);
        sample.GeneratedBuyerSeasonWageMinor.Should().Be(expected.SeasonWageMinor);
    }

    [Fact]
    public async Task A_clean_start_gives_up_the_clubs_own_leading_bid_and_sets_a_rich_clubs_cash_back_to_the_opening_cash()
    {
        var (clubId, sellerClubId, _) = await ClubsAsync();
        var openingCash = WorldRuleSet.OpeningCashMinorForTier(1);

        // A club that holds far more than the opening cash, with a bid reserved against another club's listing.
        await AddWindfallAsync(clubId, 50_000_000);

        var listingId = await ListAPlayerAsync(sellerClubId);

        (await BidAsync(clubId, listingId, 2_000_000)).Outcome.Should().Be(MarketOutcome.Found);
        (await ReservedAsync(clubId)).Should().Be(2_000_000);

        var userId = await NewManagerAsync();

        (await ClaimAsync(userId, clubId, TakeoverStarts.CleanStartCode)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        await using var read = _fixture.CreateScope();
        var readDb = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var after = await readDb.ClubAccounts.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);
        var bid = await readDb.TransferBids.AsNoTracking().SingleAsync(candidate => candidate.ListingId == listingId);
        var entry = await readDb.LedgerEntries.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId && candidate.Category == LedgerCategory.CleanStart);

        bid.Status.Should().Be(BidStatus.Released);
        after.ReservedMinor.Should().Be(0);
        after.CashMinor.Should().Be(openingCash, "the manager starts with the tier's opening cash, not with the old club's savings");
        entry.CashDeltaMinor.Should().BeNegative();
        (await readDb.TransferListings.AsNoTracking().SingleAsync(candidate => candidate.Id == listingId)).Status
            .Should().Be(ListingStatus.Open, "the other club's listing is not the clean-starting club's to cancel");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("keep")]
    public async Task Keeping_the_club_inherits_everything_as_it_stands(string? start)
    {
        var (clubId, _, _) = await ClubsAsync();

        await RunAiClubsAsync();
        await GrowTheClubAsync(clubId);
        await PutInAdministrationAsync(clubId);

        var oldPlayerIds = await ActivePlayerIdsAsync(clubId);
        var cashBefore = await CashAsync(clubId);
        var userId = await NewManagerAsync();

        (await ClaimAsync(userId, clubId, start)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await db.ClubAccounts.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);
        var stadium = await db.ClubStadiums.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);

        (await ActivePlayerIdsAsync(clubId)).Should().BeEquivalentTo(oldPlayerIds);
        account.CashMinor.Should().Be(cashBefore, "the debt comes with the club");
        account.FinancialState.Should().Be(FinancialStateKind.Administration);
        stadium.Capacity.Should().BeGreaterThan(5_000);
        (await db.LedgerEntries.AsNoTracking().CountAsync(entry => entry.ClubId == clubId && entry.Category == LedgerCategory.CleanStart)).Should().Be(0);
        (await db.Clubs.AsNoTracking().SingleAsync(candidate => candidate.Id == clubId)).StandingCentis.Should().Be(9_000);
    }

    [Fact]
    public async Task A_retried_clean_start_with_the_same_key_does_not_release_the_new_squad()
    {
        var (clubId, _, _) = await ClubsAsync();
        var userId = await NewManagerAsync();
        var key = Guid.CreateVersion7().ToString();

        // A club that already holds the opening cash has nothing for the entry to move, so it is given something to move.
        await AddWindfallAsync(clubId, 1_000_000);

        (await ClaimAsync(userId, clubId, TakeoverStarts.CleanStartCode, key)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        var squad = await ActivePlayerIdsAsync(clubId);

        (await ClaimAsync(userId, clubId, TakeoverStarts.CleanStartCode, key)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        (await ActivePlayerIdsAsync(clubId)).Should().BeEquivalentTo(squad, "a retry is answered from the first attempt");

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await db.LedgerEntries.AsNoTracking().CountAsync(entry => entry.ClubId == clubId && entry.Category == LedgerCategory.CleanStart)).Should().Be(1);
    }

    [Fact]
    public async Task A_fixture_locked_before_the_clean_start_is_played_with_the_players_it_was_locked_with()
    {
        var (clubId, _, _) = await ClubsAsync();
        var oldPlayerIds = await ActivePlayerIdsAsync(clubId);

        Guid matchdayId;
        Guid fixtureId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var entry = await db.ClubSeasonEntries.AsNoTracking().FirstAsync(candidate => candidate.ClubId == clubId);

            matchdayId = await db.Matchdays
                .Where(matchday => matchday.DivisionSeasonId == entry.DivisionSeasonId)
                .OrderBy(matchday => matchday.RoundNumber)
                .Select(matchday => matchday.Id)
                .FirstAsync();

            fixtureId = await db.Fixtures
                .Where(fixture => fixture.MatchdayId == matchdayId && (fixture.HomeClubId == clubId || fixture.AwayClubId == clubId))
                .Select(fixture => fixture.Id)
                .SingleAsync();

            await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);
        }

        var userId = await NewManagerAsync();

        (await ClaimAsync(userId, clubId, TakeoverStarts.CleanStartCode)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        await using (var scope = _fixture.CreateScope())
        {
            var resolved = await scope.ServiceProvider.GetRequiredService<ResolveMatchday>().ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

            resolved.Outcome.Should().Be(ResolveMatchdayOutcome.Resolved);
        }

        await using var read = _fixture.CreateScope();
        var readDb = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fixture = await readDb.Fixtures.AsNoTracking().SingleAsync(candidate => candidate.Id == fixtureId);
        var snapshot = await readDb.InputSnapshots.AsNoTracking().SingleAsync(candidate => candidate.FixtureId == fixtureId);

        fixture.Status.Should().Be(FixtureStatus.Staged);
        fixture.HomeScore.Should().NotBeNull();

        // The frozen document names the old players and none of the new ones (SQ-7).
        var newPlayerIds = await ActivePlayerIdsAsync(clubId);

        newPlayerIds.Should().OnlyContain(id => !snapshot.SnapshotJson.Contains(id.ToString()));
        oldPlayerIds.Count(id => snapshot.SnapshotJson.Contains(id.ToString())).Should().BeGreaterThanOrEqualTo(11);
    }

    [Fact]
    public async Task The_preview_shows_both_options_and_the_clean_start_it_shows_is_the_one_the_claim_writes()
    {
        var (clubId, _, _) = await ClubsAsync();

        await RunAiClubsAsync();
        await GrowTheClubAsync(clubId);
        await PutInAdministrationAsync(clubId);

        TakeoverPreviewResult preview;

        await using (var scope = _fixture.CreateScope())
        {
            preview = await scope.ServiceProvider.GetRequiredService<GetTakeoverPreview>().ExecuteAsync(clubId, CancellationToken.None);
        }

        preview.Outcome.Should().Be(TakeoverPreviewOutcome.Found);

        var keep = preview.Preview!.Keep;
        var clean = preview.Preview.CleanStart;

        keep.Start.Should().Be("keep");
        keep.FinancialState.Should().Be("administration");
        keep.CashMinor.Should().BeNegative();
        keep.DebtMinor.Should().Be(-keep.CashMinor);
        keep.GroundPlaces.Should().BeGreaterThan(5_000);
        keep.SquadStrength.Should().BeGreaterThan(0);

        clean.Start.Should().Be("clean_start");
        clean.FinancialState.Should().Be("healthy");
        clean.CashMinor.Should().Be(WorldRuleSet.OpeningCashMinorForTier(1));
        clean.DebtMinor.Should().Be(0);
        clean.GroundPlaces.Should().Be(5_000);
        clean.GroundLevel.Should().Be(1);
        clean.SnackBarLevel.Should().Be(1);
        clean.ShopLevel.Should().Be(1);
        clean.SeasonIncomeForecastMinor.Should().BeLessThan(keep.SeasonIncomeForecastMinor, "the grown ground and fans are gone");

        var userId = await NewManagerAsync();

        (await ClaimAsync(userId, clubId, TakeoverStarts.CleanStartCode)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        (await ActivePlayerIdsAsync(clubId)).Should().HaveCount(clean.Players);
        (await WageBillAsync(clubId)).Should().Be(clean.SeasonWageMinor);

        var basis = await BasisAsync(clubId);

        basis.ForecastMinor.Should().Be(clean.SeasonIncomeForecastMinor, "the preview forecasts what the club then earns");
        basis.WageBudgetMinor.Should().Be(clean.WageBudgetMinor);
        basis.OverdraftLimitMinor.Should().Be(clean.OverdraftLimitMinor);
        (await OverdraftLimitAsync(clubId)).Should().Be(clean.OverdraftLimitMinor);

        await using var scope2 = _fixture.CreateScope();
        var strengths = await scope2.ServiceProvider
            .GetRequiredService<Tactical11Manager.Application.Abstractions.Fans.IFanQueries>()
            .GetSquadStrengthsAsync([clubId], CancellationToken.None);

        strengths[clubId].Should().BeCloseTo((int)(clean.SquadStrength * 100), 15, "the strength shown, to a tenth, is the strength the club has");
    }

    [Fact]
    public async Task A_club_a_manager_holds_has_no_preview()
    {
        var (clubId, _, _) = await ClubsAsync();
        var userId = await NewManagerAsync();

        (await ClaimAsync(userId, clubId, null)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        await using var scope = _fixture.CreateScope();
        var preview = await scope.ServiceProvider.GetRequiredService<GetTakeoverPreview>().ExecuteAsync(clubId, CancellationToken.None);

        preview.Outcome.Should().Be(TakeoverPreviewOutcome.ClubAlreadyClaimed);
        preview.Preview.Should().BeNull();
    }

    // ---- arrangement ------------------------------------------------------------------------------------------------------------------

    /// <summary>Three free clubs of Spain: the one that starts clean, a bidder or seller, and a spare.</summary>
    private async Task<(Guid Club, Guid Other, Guid Spare)> ClubsAsync()
    {
        await using var scope = _fixture.CreateScope();
        var countryId = _fixture.Countries.Single(country => country.Code == "ESP").Id;
        var ids = await ClubIdsOfAsync(scope, countryId, CancellationToken.None);

        return (ids[0], ids[1], ids[2]);
    }

    private async Task<Guid> NewManagerAsync()
    {
        await using var scope = _fixture.CreateScope();

        return await CreateManagerAsync(scope, CancellationToken.None);
    }

    private async Task<ClaimClubResult> ClaimAsync(Guid userId, Guid clubId, string? start, string? key = null)
    {
        await using var scope = _fixture.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ClaimClub>()
            .ExecuteAsync(userId, new ClaimClubRequest { ClubId = clubId, Start = start }, key ?? Guid.CreateVersion7().ToString(), CancellationToken.None);
    }

    private async Task RunAiClubsAsync()
    {
        await using var scope = _fixture.CreateScope();

        await scope.ServiceProvider.GetRequiredService<EvaluateAiClubs>().ExecuteAsync(CancellationToken.None);
    }

    /// <summary>Grows the ground and the buildings, raises the standing and leaves a draft team sheet naming the squad.</summary>
    private async Task GrowTheClubAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        (await db.ClubStadiums.SingleAsync(candidate => candidate.ClubId == clubId)).AddSeats(StadiumStand.Standing, 20_000, now);

        var buildings = await db.ClubStructures.Where(candidate => candidate.ClubId == clubId).ToListAsync();

        buildings[0].StartUpgrade(now.AddDays(1), now);
        buildings[0].CompleteUpgrade(now.AddDays(1));
        buildings[0].StartUpgrade(now.AddDays(9), now.AddDays(2));
        buildings[1].StartUpgrade(now.AddDays(9), now);

        (await db.Clubs.SingleAsync(candidate => candidate.Id == clubId)).RecordStanding(9_000, now);

        var plan = await db.TacticalPlans.SingleAsync(candidate => candidate.ClubId == clubId && candidate.IsDefault);
        var fixtureId = await db.Fixtures
            .Where(fixture => fixture.HomeClubId == clubId || fixture.AwayClubId == clubId)
            .OrderBy(fixture => fixture.KickoffAt)
            .Select(fixture => fixture.Id)
            .FirstAsync();
        var sheet = FixtureTeamSheet.Draft(Guid.CreateVersion7(), fixtureId, clubId, plan.Id, plan.Version, now);
        var goalkeeper = await db.TacticalSlots.Where(slot => slot.PlanId == plan.Id && slot.AssignedPlayerId != null).OrderBy(slot => slot.SlotNumber).FirstAsync();

        db.FixtureTeamSheets.Add(sheet);
        db.TeamSheetEntries.Add(TeamSheetEntry.Select(Guid.CreateVersion7(), sheet.Id, goalkeeper.AssignedPlayerId!.Value, TeamSheetDesignation.Starter, goalkeeper.SlotNumber, null, now));

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Takes a club's cash below minus its overdraft limit through a recurring cost, as the matchday would, and records the state.</summary>
    private async Task PutInAdministrationAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);
        var now = _fixture.Clock.UtcNow;
        const long limit = 5_000_000;

        account.SetOverdraft(limit, now);

        db.LedgerEntries.Add(account.Post(
            LedgerPostings.OperatingCost(Guid.CreateVersion7(), clubId, new DateOnly(2026, 9, 1), account.CashMinor + limit + 1_000_000),
            now));

        account.RecordState(ClubFinancialState.Next(FinancialStateKind.Healthy, 0, 5, account.CashMinor, limit), now);

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task AddWindfallAsync(Guid clubId, long amountMinor)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);

        db.LedgerEntries.Add(account.Post(
            LedgerPostings.Compensation(Guid.CreateVersion7(), clubId, amountMinor, $"test-windfall:{Guid.CreateVersion7():N}"),
            _fixture.Clock.UtcNow));

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<ClubFinanceBasis> BasisAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var bases = await scope.ServiceProvider.GetRequiredService<IFinanceQueries>().GetClubFinanceBasisAsync([clubId], CancellationToken.None);

        return bases.Single();
    }

    private async Task<long> OverdraftLimitAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return (await db.ClubAccounts.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId)).OverdraftLimitMinor;
    }

    private async Task<Guid> ListAPlayerWithABidAsync(Guid sellerClubId, Guid bidderClubId)
    {
        var listingId = await ListAPlayerAsync(sellerClubId);

        (await BidAsync(bidderClubId, listingId, 1_000_000)).Outcome.Should().Be(MarketOutcome.Found);

        return listingId;
    }

    private async Task<Guid> ListAPlayerAsync(Guid sellerClubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var playerId = await db.PlayerContracts
            .Where(contract => contract.ClubId == sellerClubId && contract.Status == ContractStatus.Active)
            .OrderBy(contract => contract.PlayerId)
            .Select(contract => contract.PlayerId)
            .FirstAsync();

        var listing = TransferListing.Open(
            Guid.CreateVersion7(),
            playerId,
            sellerClubId,
            1_000_000,
            generatedBuyerSeasonWageMinor: 50_000,
            generatedContractSeasons: 2,
            opensAt: now.AddDays(-3),
            endsAt: now.AddDays(1),
            idempotencyKey: null,
            now: now);

        db.TransferListings.Add(listing);
        await db.SaveChangesAsync(CancellationToken.None);

        return listing.Id;
    }

    private async Task<BidWriteResult> BidAsync(Guid bidderClubId, Guid listingId, long amountMinor)
    {
        await using var scope = _fixture.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<IBidWriter>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Bids take a transaction-scoped listing lock, so the writer is called inside a transaction (ADR-0026).
        await using var transaction = await unitOfWork.BeginTransactionAsync(TransactionIsolation.ReadCommitted, CancellationToken.None);

        var write = await writer.BidAsync(bidderClubId, MarketActor.Service("clean-start-test"), listingId, amountMinor, idempotencyKey: null, CancellationToken.None);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        return write;
    }

    private async Task<List<Guid>> ActivePlayerIdsAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.PlayerContracts.AsNoTracking()
            .Where(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active)
            .Select(contract => contract.PlayerId)
            .ToListAsync();
    }

    private async Task<long> WageBillAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.PlayerContracts.AsNoTracking()
            .Where(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active)
            .SumAsync(contract => contract.SeasonWageMinor);
    }

    private async Task<long> ReservedAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return (await db.ClubAccounts.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId)).ReservedMinor;
    }

    private async Task<long> CashAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return (await db.ClubAccounts.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId)).CashMinor;
    }
}

/// <summary>The clean-start tests' own seeded world, one per test.</summary>
public sealed class CleanStartFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public CleanStartFixture()
        : base("tactical11_clean_start")
    {
    }
}

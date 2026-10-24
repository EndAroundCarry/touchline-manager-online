using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Application.World;
using Tactical11Manager.Domain.Auth;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.World;

namespace Tactical11Manager.Infrastructure.Tests.Competition;

/// <summary>
/// One season's rollover against real PostgreSQL 17 (`PR-1`…`PR-6`, master plan §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// <para>
/// Its own world per test, because a rollover rewrites which tier every club plays in and the shared world
/// collection asserts what a freshly seeded pyramid looks like. The arrangement is the real one: a seeded
/// world with its two seeded tiers, every matchday of the closing season published, and the worker's
/// use case — not a hand-built shortcut — doing the movement.
/// </para>
/// <para>
/// Publication is fabricated rather than simulated, and that is a deliberate cost choice: the point of this
/// suite is movement, history, and resumability, not the engine (whose determinism Stage 5 pins), so the
/// closing season's fixtures are marked published with a deterministic score and the standings are rebuilt
/// from them with the same tool the live publication uses (`TBL-13`). That keeps a two-tier rollover a few
/// seconds rather than a few minutes of simulated football.
/// </para>
/// </remarks>
public sealed class SeasonRolloverTests : IAsyncLifetime, IDisposable
{
    /// <summary>Every country opens with tiers 1 and 2 (`WORLD-5`).</summary>
    private static readonly int ClubsInWorld = LaunchCountries.All.Count * WorldRuleSet.SeededTiers * WorldRuleSet.ClubsPerDivision;

    private readonly RolloverFixture _fixture = new();

    /// <summary>Starts a freshly seeded world, which already holds tiers 1 and 2 in every country.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_finished_season_rolls_over_promoting_and_relegating_and_opening_the_next_season()
    {
        Guid seasonId;
        Guid countryId;
        Guid tierOneDivisionId;
        Guid tierTwoDivisionId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            seasonId = await db.Seasons
                .Where(candidate => candidate.WorldId == _fixture.WorldId)
                .Select(candidate => candidate.Id)
                .SingleAsync();
            countryId = await db.Countries
                .OrderBy(country => country.SortOrder)
                .Select(country => country.Id)
                .FirstAsync();
            tierOneDivisionId = await db.Divisions
                .Where(division => division.CountryId == countryId && division.TierNumber == 1)
                .Select(division => division.Id)
                .SingleAsync();
            tierTwoDivisionId = await db.Divisions
                .Where(division => division.CountryId == countryId && division.TierNumber == 2)
                .Select(division => division.Id)
                .SingleAsync();
        }

        await using (var scope = _fixture.CreateScope())
        {
            var rollover = scope.ServiceProvider.GetRequiredService<RunSeasonRollover>();

            // The season has not been played yet, so preflight refuses it. The refusal is transient — the
            // exception is not a permanent job failure — and the rollover row is left at its first checkpoint
            // for the retry to resume.
            await FluentActions
                .Awaiting(() => rollover.ExecuteAsync(seasonId, CancellationToken.None))
                .Should()
                .ThrowAsync<InvalidOperationException>("PR-4: an unplayed season cannot roll over");

            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var rolloverRow = await db.SeasonRollovers.SingleAsync(row => row.SeasonId == seasonId);

            rolloverRow.Phase.Should().Be(SeasonRolloverPhase.Started, "the freeze never ran");
            (await db.Seasons.SingleAsync(candidate => candidate.Id == seasonId)).Status
                .Should().Be(SeasonStatus.Active, "claims are still open because the season did not freeze");
        }

        await PublishSeasonAsync(seasonId);

        // The expected movement, read from the closing standings the fabricated results produced.
        List<Guid> tierOneOrder;
        List<Guid> tierTwoOrder;

        await using (var scope = _fixture.CreateScope())
        {
            tierOneOrder = await RankedClubsAsync(scope, tierOneDivisionId, seasonId);
            tierTwoOrder = await RankedClubsAsync(scope, tierTwoDivisionId, seasonId);
        }

        var expectedPromoted = tierTwoOrder.Take(WorldRuleSet.DirectPromotionPlaces).ToList();
        var expectedRelegated = tierOneOrder.TakeLast(WorldRuleSet.DirectRelegationPlaces).ToList();

        // One of the clubs that will move is held by a human, so the movement message has someone to reach;
        // every other moving club is AI and is told nothing (COM-1).
        var movedClubId = expectedPromoted[0];
        var movedManagerId = await AttachManagerAsync(movedClubId);

        SeasonRolloverResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);
        }

        result.Outcome.Should().Be(SeasonRolloverOutcome.Completed);
        result.NextSeasonId.Should().NotBeNull();
        result.Promotions.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces, "PR-1: two up between the two tiers of each country, as no play-off is stored yet");
        result.Relegations.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces, "PR-1: two down");

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var world = await db.GameWorlds.SingleAsync();
            var closing = await db.Seasons.SingleAsync(candidate => candidate.Id == seasonId);
            var next = await db.Seasons.SingleAsync(candidate => candidate.Id == result.NextSeasonId!.Value);

            world.CurrentSeasonNumber.Should().Be(2, "TIME-3: the game year advances at rollover");
            closing.Status.Should().Be(SeasonStatus.Completed, "PR-6: history from here is immutable");
            next.SequenceNumber.Should().Be(2);
            next.GameYear.Should().Be(closing.GameYear + 1);
            next.Status.Should().Be(SeasonStatus.Active, "onboarding reopens in the next season");
            SeasonCalendar.IsMatchday(DateOnly.FromDateTime(next.StartsAt.UtcDateTime))
                .Should()
                .BeTrue("CAL-6: the next season starts on the first matchday after the rollover window");

            // Every active tier has a next-season instance holding a full division, a full-length
            // schedule, and an opening table.
            var nextDivisionSeasons = await db.DivisionSeasons
                .Where(divisionSeason => divisionSeason.SeasonId == next.Id)
                .ToListAsync();

            nextDivisionSeasons.Should().HaveCount(LaunchCountries.All.Count * WorldRuleSet.SeededTiers, "two seeded tiers in each of nine countries");

            foreach (var divisionSeason in nextDivisionSeasons)
            {
                (await db.ClubSeasonEntries.CountAsync(entry => entry.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(WorldRuleSet.ClubsPerDivision, "PR-1: a tier holds a full division of clubs");
                (await db.Matchdays.CountAsync(matchday => matchday.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(WorldRuleSet.MatchdaysPerSeason, "CAL-1");
                (await db.Standings.CountAsync(standing => standing.DivisionSeasonId == divisionSeason.Id))
                    .Should()
                    .Be(WorldRuleSet.ClubsPerDivision);
            }

            // The closing entries carry their final rank and movement flags exactly once (PR-4).
            var closingEntries = await db.ClubSeasonEntries
                .Where(entry => entry.SeasonId == seasonId)
                .ToListAsync();

            closingEntries.Should().OnlyContain(entry => entry.FinalRank != null, "every entry was closed");
            closingEntries.Count(entry => entry.IsPromoted).Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces, "two up in each of nine countries");
            closingEntries.Count(entry => entry.IsRelegated).Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces);

            foreach (var clubId in expectedPromoted)
            {
                closingEntries.Single(entry => entry.ClubId == clubId).IsPromoted.Should().BeTrue();
            }

            foreach (var clubId in expectedRelegated)
            {
                closingEntries.Single(entry => entry.ClubId == clubId).IsRelegated.Should().BeTrue();
            }

            // The promoted clubs sit in the next season's top tier and the relegated ones in the second.
            var nextTierOneSeason = await db.DivisionSeasons.SingleAsync(candidate =>
                candidate.DivisionId == tierOneDivisionId && candidate.SeasonId == next.Id);
            var nextTierTwoSeason = await db.DivisionSeasons.SingleAsync(candidate =>
                candidate.DivisionId == tierTwoDivisionId && candidate.SeasonId == next.Id);

            var nextTierOneClubs = await db.ClubSeasonEntries
                .Where(entry => entry.DivisionSeasonId == nextTierOneSeason.Id)
                .Select(entry => entry.ClubId)
                .ToListAsync();
            var nextTierTwoClubs = await db.ClubSeasonEntries
                .Where(entry => entry.DivisionSeasonId == nextTierTwoSeason.Id)
                .Select(entry => entry.ClubId)
                .ToListAsync();

            nextTierOneClubs.Should().Contain(expectedPromoted, "PR-3: movement follows the club");
            nextTierOneClubs.Should().NotContain(expectedRelegated);
            nextTierTwoClubs.Should().Contain(expectedRelegated);
            nextTierTwoClubs.Should().NotContain(expectedPromoted);

            // A club's season history reads the entry the rollover closed and never rewrote (PR-4, PR-6). The
            // promoted club's line names the season, the tier it left, its final rank, and its movement.
            var promotedClubId = expectedPromoted[0];
            var history = await scope.ServiceProvider.GetRequiredService<GetClubSeasonHistory>()
                .ExecuteAsync(promotedClubId, CancellationToken.None);

            history.Outcome.Should().Be(CompetitionReadOutcome.Found);
            history.History!.ClubId.Should().Be(promotedClubId);
            history.History.Seasons.Should().HaveCount(1, "only the closing season has finished");

            var finished = history.History.Seasons[0];
            finished.SeasonNumber.Should().Be(1);
            finished.TierNumber.Should().Be(2, "the club played the closing season in the second tier");
            finished.FinalRank.Should().BeInRange(1, 3, "it was promoted from the top three");
            finished.Promoted.Should().BeTrue();
            finished.ClosingReputation.Should().BeGreaterThan(0);
            finished.ClosingCashMinor.Should().BeGreaterThan(0);
            history.History.NextSeason.Should().BeNull(
                "the world pointer has advanced, so no next season exists yet to be placed in (PR-5)");

            // The manager whose club moved is told where it went, and the AI clubs are not (PR-1, COM-1).
            var movementMessages = await db.InboxMessages
                .Where(message => message.TemplateKey == InboxTemplates.SeasonMovement)
                .ToListAsync();

            movementMessages.Should().ContainSingle("only the club a manager held was told it moved");
            movementMessages[0].Category.Should().Be(InboxCategory.Table);
            movementMessages[0].RecipientManagerId.Should().Be(movedManagerId);
            movementMessages[0].RelatedEntityId.Should().BeNull();

            var rendered = InboxMessageText.Render(
                movementMessages[0].TemplateKey,
                movementMessages[0].ParametersJson);

            rendered.Title.Should().StartWith("Promoted", "the club went up from the second tier");

            // The closing season's own schedule is untouched history (PR-6).
            var closingMatchdayIds = await db.Matchdays
                .Where(matchday => matchday.DivisionSeasonId ==
                    db.DivisionSeasons.Single(ds => ds.DivisionId == tierOneDivisionId && ds.SeasonId == seasonId).Id)
                .Select(matchday => matchday.Id)
                .ToListAsync();

            (await db.Fixtures.CountAsync(fixture => closingMatchdayIds.Contains(fixture.MatchdayId)
                && fixture.Status == FixtureStatus.Published))
                .Should()
                .Be(WorldRuleSet.MatchdaysPerSeason * (WorldRuleSet.ClubsPerDivision / 2), "the played season's results are the record of what happened");

            // The rollover settles the closing season's money (FIN-5, master plan §6.8): one final-position
            // award and one finance summary for every closing club.
            (await db.LedgerEntries.CountAsync(entry => entry.Category == LedgerCategory.PositionAward))
                .Should()
                .Be(ClubsInWorld, "every closing club earns a final-position award");
            (await db.ClubSeasonFinances.CountAsync(summary => summary.SeasonId == seasonId))
                .Should()
                .Be(ClubsInWorld, "every closing club has a season finance summary");

            // A relegated club is given its parachute, once, and every club's standing takes its step (FIN-5, STD-2, STD-3).
            var relegatedClubIds = await db.ClubSeasonEntries
                .Where(entry => entry.SeasonId == seasonId && entry.IsRelegated)
                .Select(entry => entry.ClubId)
                .ToListAsync();

            relegatedClubIds.Should().NotBeEmpty();
            (await db.LedgerEntries.Where(entry => entry.Category == LedgerCategory.Parachute).Select(entry => entry.ClubId).ToListAsync())
                .Should()
                .BeEquivalentTo(relegatedClubIds, "only a relegated club is given a parachute");

            var steps = await db.ClubStandingEvents.Where(step => step.SeasonId == seasonId).ToListAsync();

            steps.Should().HaveCount(ClubsInWorld, "every closing club's standing moved once");
            steps.Where(step => step.NextTier > step.FinishedTier).Select(step => step.ClubId)
                .Should()
                .BeEquivalentTo(relegatedClubIds, "the step knows which clubs went down");
            steps.Where(step => step.FinalPlace == 1)
                .Should()
                .OnlyContain(step => step.AfterCentis > step.BeforeCentis, "a champion gains standing");

            // The closing season's sponsor deals are settled against the final table, and the next season's sponsors have made their
            // offers; a computer club has already signed, and a club a manager holds waits for the manager's choice (SPN-1, SPN-4, OBJ-4).
            (await db.SponsorDeals.CountAsync(deal => deal.SeasonId == seasonId && deal.Outcome != null))
                .Should()
                .Be(ClubsInWorld, "every closing club's objective was settled");

            var nextSeasonId = await db.Seasons
                .Where(candidate => candidate.WorldId == _fixture.WorldId && candidate.SequenceNumber == 2)
                .Select(candidate => candidate.Id)
                .SingleAsync();

            var managedClubs = await db.ClubTenures.CountAsync(tenure => tenure.EndedAt == null);

            (await db.SponsorOffers.CountAsync(offer => offer.SeasonId == nextSeasonId)).Should().Be(ClubsInWorld * 3);
            (await db.SponsorDeals.CountAsync(deal => deal.SeasonId == nextSeasonId && deal.SignedBy == SponsorSigner.Ai))
                .Should()
                .Be(ClubsInWorld - managedClubs, "a computer club signs at once");
            (await db.InboxMessages.CountAsync(message => message.TemplateKey == FinanceInboxTemplates.SponsorOffersReady))
                .Should()
                .Be(managedClubs, "each manager is told what is on offer");

            // The clubs are unmanaged, so the AI renewed their expiring contracts (CON-6).
            (await db.PlayerContracts.CountAsync(contract =>
                contract.Status == ContractStatus.Closed
                && contract.ClosedReason == PlayerContractCloseReasons.Renewed))
                .Should()
                .BeGreaterThan(0, "an unmanaged club renews its expiring players");
        }

        // A redelivered rollover is a no-op: the next season stays the second one and nothing moves twice.
        await using (var scope = _fixture.CreateScope())
        {
            var again = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);

            again.Outcome.Should().Be(SeasonRolloverOutcome.AlreadyCompleted);
        }

        await using (var final = _fixture.CreateScope())
        {
            var db = final.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.GameWorlds.SingleAsync()).CurrentSeasonNumber.Should().Be(2, "the year advanced once");
            (await db.Seasons.CountAsync(candidate => candidate.WorldId == _fixture.WorldId))
                .Should()
                .Be(2, "exactly one next season was created");
            (await db.SeasonRollovers.CountAsync(row => row.SeasonId == seasonId)).Should().Be(1);
            (await db.ClubSeasonEntries.CountAsync(entry => entry.SeasonId == seasonId && entry.IsPromoted))
                .Should()
                .Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces, "the movement was not re-applied");
            (await db.InboxMessages.CountAsync(message => message.TemplateKey == InboxTemplates.SeasonMovement))
                .Should()
                .Be(1, "the movement was not announced twice");
        }
    }

    [Fact]
    public async Task Rolling_over_gives_every_clubs_fans_their_verdict_and_moves_them_with_the_club()
    {
        Guid seasonId;
        Guid tierOneDivisionId;
        Guid tierTwoDivisionId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var countryId = await db.Countries.OrderBy(country => country.SortOrder).Select(country => country.Id).FirstAsync();

            seasonId = await db.Seasons.Where(candidate => candidate.WorldId == _fixture.WorldId).Select(candidate => candidate.Id).SingleAsync();
            tierOneDivisionId = await db.Divisions
                .Where(division => division.CountryId == countryId && division.TierNumber == 1)
                .Select(division => division.Id)
                .SingleAsync();
            tierTwoDivisionId = await db.Divisions
                .Where(division => division.CountryId == countryId && division.TierNumber == 2)
                .Select(division => division.Id)
                .SingleAsync();
        }

        await PublishSeasonAsync(seasonId);

        // Each of the two tiers' clubs gets fans and a target of its own, so the verdict is a different number for almost every club.
        var finalRank = new Dictionary<Guid, int>();
        var tierOf = new Dictionary<Guid, int>();
        var before = new Dictionary<Guid, FanBaseState>();
        var targetOf = new Dictionary<Guid, int>();
        List<Guid> tierOneOrder;
        List<Guid> tierTwoOrder;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            tierOneOrder = await RankedClubsAsync(scope, tierOneDivisionId, seasonId);
            tierTwoOrder = await RankedClubsAsync(scope, tierTwoDivisionId, seasonId);

            foreach (var (order, tier) in new[] { (tierOneOrder, 1), (tierTwoOrder, 2) })
            {
                for (var index = 0; index < order.Count; index++)
                {
                    finalRank[order[index]] = index + 1;
                    tierOf[order[index]] = tier;
                }
            }

            var position = 0;
            var bases = await db.ClubFanBases.Where(fanBase => finalRank.Keys.Contains(fanBase.ClubId)).ToListAsync();

            foreach (var fanBase in bases.OrderBy(candidate => candidate.ClubId))
            {
                position++;

                var state = new FanBaseState(
                    FanRuleSet.StartFansFor(tierOf[fanBase.ClubId]) + (position * 1_000),
                    2_000 + ((position * 700) % 7_000),
                    LastWeekGap: position % 5);

                targetOf[fanBase.ClubId] = ((position * 5) % WorldRuleSet.ClubsPerDivision) + 1;
                fanBase.Apply(state, _fixture.Clock.UtcNow);
                fanBase.SetTarget(targetOf[fanBase.ClubId], _fixture.Clock.UtcNow);
                before[fanBase.ClubId] = state;
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }

        SeasonRolloverResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>().ExecuteAsync(seasonId, CancellationToken.None);
        }

        result.Outcome.Should().Be(SeasonRolloverOutcome.Completed);

        var promoted = tierTwoOrder.Take(WorldRuleSet.DirectPromotionPlaces).ToHashSet();
        var relegated = tierOneOrder.TakeLast(WorldRuleSet.DirectRelegationPlaces).ToHashSet();

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var after = await db.ClubFanBases.AsNoTracking().Where(fanBase => finalRank.Keys.Contains(fanBase.ClubId)).ToDictionaryAsync(fanBase => fanBase.ClubId);
            var resets = await db.ClubFanEvents.AsNoTracking()
                .Where(fanEvent => fanEvent.Kind == FanEventKind.SeasonReset)
                .ToDictionaryAsync(fanEvent => fanEvent.ClubId);

            resets.Should().HaveCount(await db.Clubs.CountAsync(), "every club's fans in the world are closed for the season exactly once");

            foreach (var (clubId, rank) in finalRank)
            {
                var tier = tierOf[clubId];
                var gap = rank - targetOf[clubId];
                var closed = FanRuleSet.AfterSeason(before[clubId], tier, gap);
                var expected = promoted.Contains(clubId) ? FanRuleSet.OnTierChange(closed, 2, 1)
                    : relegated.Contains(clubId) ? FanRuleSet.OnTierChange(closed, 1, 2)
                    : closed;

                after[clubId].Fans.Should().Be(expected.Fans, $"club {clubId:N} finished {rank} of tier {tier} against a target of {targetOf[clubId]}");
                after[clubId].MoodBp.Should().Be(expected.MoodBp);
                after[clubId].LastWeekGap.Should().BeNull("the new season is judged afresh");
                after[clubId].SeasonTargetRank.Should().BeInRange(1, WorldRuleSet.ClubsPerDivision, "the fans pick the place they hope for from the squads");

                resets[clubId].SeasonId.Should().Be(seasonId);
                resets[clubId].Gap.Should().Be(gap);
                resets[clubId].FansAfter.Should().Be(closed.Fans);
                resets[clubId].MoodAfterBp.Should().Be(closed.MoodBp);
            }

            var moves = await db.ClubFanEvents.AsNoTracking()
                .Where(fanEvent => fanEvent.Kind == FanEventKind.Promotion || fanEvent.Kind == FanEventKind.Relegation)
                .ToListAsync();

            var promotions = moves.Where(fanEvent => fanEvent.Kind == FanEventKind.Promotion).Select(fanEvent => fanEvent.ClubId).ToList();
            var relegations = moves.Where(fanEvent => fanEvent.Kind == FanEventKind.Relegation).Select(fanEvent => fanEvent.ClubId).ToList();

            promotions.Should().HaveCount(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces).And.Contain(promoted, "the top two of the lower tier climb");
            relegations.Should().HaveCount(LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces).And.Contain(relegated, "the bottom two of the upper tier fall");
            moves.Should().OnlyContain(fanEvent => fanEvent.SeasonId == result.NextSeasonId!.Value);
        }
    }

    [Fact]
    public async Task A_preview_reports_not_ready_until_the_season_is_finished_then_the_movement_and_awards()
    {
        Guid seasonId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            seasonId = await db.Seasons
                .Where(candidate => candidate.WorldId == _fixture.WorldId)
                .Select(candidate => candidate.Id)
                .SingleAsync();

            var before = await scope.ServiceProvider.GetRequiredService<PreviewSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);

            before.Should().NotBeNull();
            before!.Ready.Should().BeFalse("PR-4: an unplayed season cannot roll over");
            before.Preflight.UnpublishedMatchdays.Should().BeGreaterThan(0);
            before.RolloverPhase.Should().BeNull("no rollover has started");
        }

        await PublishSeasonAsync(seasonId);

        await using (var scope = _fixture.CreateScope())
        {
            var preview = await scope.ServiceProvider.GetRequiredService<PreviewSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);

            preview.Should().NotBeNull();
            preview!.Ready.Should().BeTrue();
            preview.Preflight.UnpublishedMatchdays.Should().Be(0);
            preview.Preflight.Reconciles.Should().BeTrue();
            preview.NextSeasonExists.Should().BeFalse();
            preview.Totals.Countries.Should().Be(LaunchCountries.All.Count);
            preview.Totals.Clubs.Should().Be(ClubsInWorld, "two seeded tiers in each of nine countries");
            preview.Totals.Promotions.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces, "PR-1: two up between the two adjacent tiers of each country, as no play-off is stored yet");
            preview.Totals.Relegations.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces, "PR-1: two down");

            // Every rank in every active tier earns a position award (FIN-5), and the preview totals them
            // without paying them.
            var expectedAwards = LaunchCountries.All.Count * (SumPositionAwards(tier: 1) + SumPositionAwards(tier: 2));
            preview.Totals.PositionAwardsMinor.Should().Be(expectedAwards);

            // The preview is a read (ADR-0020): no rollover row and no award was written.
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            (await db.SeasonRollovers.CountAsync()).Should().Be(0);
            (await db.LedgerEntries.CountAsync(entry => entry.Category == LedgerCategory.PositionAward))
                .Should()
                .Be(0);
        }
    }

    [Fact]
    public async Task A_failed_rollover_is_resumed_after_reconciliation_and_moves_each_club_once()
    {
        Guid seasonId;
        Guid divisionSeasonId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            seasonId = await db.Seasons
                .Where(candidate => candidate.WorldId == _fixture.WorldId)
                .Select(candidate => candidate.Id)
                .SingleAsync();
            divisionSeasonId = await db.DivisionSeasons
                .Where(divisionSeason => divisionSeason.SeasonId == seasonId)
                .OrderBy(divisionSeason => divisionSeason.Id)
                .Select(divisionSeason => divisionSeason.Id)
                .FirstAsync();
        }

        await PublishSeasonAsync(seasonId);

        // Corrupt one table row so the projections no longer reconcile — the defect that fails a rollover
        // and dead-letters its job (TBL-13, ADR-0031).
        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            await db.Database.ExecuteSqlRawAsync(
                "update competition.standings set yellow_cards = yellow_cards + 1 "
                + "where division_season_id = {0}",
                divisionSeasonId);

            await FluentActions
                .Awaiting(() => scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                    .ExecuteAsync(seasonId, CancellationToken.None))
                .Should()
                .ThrowAsync<PermanentJobFailureException>("a projection drift is a defect, not a transient fault");

            db.ChangeTracker.Clear();

            var row = await db.SeasonRollovers.SingleAsync(candidate => candidate.SeasonId == seasonId);
            row.Phase.Should().Be(SeasonRolloverPhase.Failed);
            row.FailureReason.Should().NotBeNullOrWhiteSpace();
        }

        // An operator repairs the projection, then resumes the rollover.
        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var rebuilt = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
                .ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);
            rebuilt.Outcome.Should().Be(ProjectionRebuildOutcome.Rebuilt);

            var resume = await scope.ServiceProvider.GetRequiredService<ResumeSeasonRollover>()
                .ExecuteAsync(seasonId, "reconciled the corrupted table row", CancellationToken.None);

            resume.Outcome.Should().Be(ResumeSeasonRolloverOutcome.Resumed);
            resume.Requeued.Should().BeFalse("the rollover was run directly, so no job row was dead-lettered");
            resume.BusinessKey.Should().Be(SeasonRolloverJobTypes.RolloverKey(seasonId));

            // The fallback enqueued the job the worker would claim, and the resume is audited with its reason.
            (await db.Jobs.SingleAsync(job => job.BusinessKey == resume.BusinessKey)).Status.Should().Be("pending");

            var audited = await db.AuditEntries
                .SingleAsync(entry => entry.Action == WorldAuditActions.RolloverResumed);
            audited.Reason.Should().Be("reconciled the corrupted table row");
        }

        // The worker runs the resumed job, and the rollover completes exactly once.
        await using (var scope = _fixture.CreateScope())
        {
            var resumed = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);

            resumed.Outcome.Should().Be(SeasonRolloverOutcome.Completed);
        }

        await using (var final = _fixture.CreateScope())
        {
            var db = final.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.GameWorlds.SingleAsync()).CurrentSeasonNumber.Should().Be(2, "the year advanced once");
            (await db.Seasons.CountAsync(candidate => candidate.WorldId == _fixture.WorldId)).Should().Be(2);
            (await db.SeasonRollovers.CountAsync(candidate => candidate.SeasonId == seasonId)).Should().Be(1);
            (await db.ClubSeasonEntries.CountAsync(entry => entry.SeasonId == seasonId && entry.IsPromoted))
                .Should()
                .Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces, "no club was promoted twice");
            (await db.ClubSeasonEntries.CountAsync(entry => entry.SeasonId == seasonId && entry.IsRelegated))
                .Should()
                .Be(LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces);
        }
    }

    [Fact]
    public async Task A_play_off_challenger_that_wins_the_final_goes_up_and_the_14th_goes_down_with_the_wording_and_the_parachute()
    {
        var (seasonId, countryId) = await SeasonAndFirstCountryAsync();

        await PublishSeasonAsync(seasonId);

        var playoff = await PlayPlayoffAsync(seasonId, countryId, decideFinal: true, challengerWins: true);
        var managerId = await AttachManagerAsync(playoff.Challenger);

        SeasonRolloverResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);
        }

        result.Promotions.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces + 1, "the country with a won final sends three up; the others two");
        result.Relegations.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces + 1, "and takes three down");

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var nextSeasonId = result.NextSeasonId!.Value;

            (await NextLeagueClubsAsync(db, playoff.ParentDivisionId, nextSeasonId)).Should().HaveCount(WorldRuleSet.ClubsPerDivision)
                .And.Contain(playoff.Challenger).And.NotContain(playoff.Holder);
            (await NextLeagueClubsAsync(db, playoff.FeederDivisionId, nextSeasonId)).Should().HaveCount(WorldRuleSet.ClubsPerDivision)
                .And.Contain(playoff.Holder).And.NotContain(playoff.Challenger).And.Contain(playoff.SemiLoser);

            var entries = await db.ClubSeasonEntries.Where(entry => entry.SeasonId == seasonId).ToListAsync();

            entries.Single(entry => entry.ClubId == playoff.Challenger).IsPromoted.Should().BeTrue();
            entries.Single(entry => entry.ClubId == playoff.Holder).IsRelegated.Should().BeTrue();
            entries.Single(entry => entry.ClubId == playoff.SemiLoser).Should().Match<ClubSeasonEntry>(entry => !entry.IsPromoted && !entry.IsRelegated);

            // The final loser is treated as relegated: a parachute, and a standing step that knows it went down (FIN-5, STD-3).
            (await db.LedgerEntries.Where(entry => entry.Category == LedgerCategory.Parachute).Select(entry => entry.ClubId).ToListAsync())
                .Should().Contain(playoff.Holder).And.HaveCount(result.Relegations);
            (await db.ClubStandingEvents.SingleAsync(step => step.SeasonId == seasonId && step.ClubId == playoff.Holder))
                .NextTier.Should().BeGreaterThan(1);

            var message = await db.InboxMessages.SingleAsync(candidate =>
                candidate.TemplateKey == InboxTemplates.SeasonMovement && candidate.RecipientManagerId == managerId);
            var text = InboxMessageText.Render(message.TemplateKey, message.ParametersJson);

            text.Title.Should().StartWith("Promoted");
            text.Body.Should().Contain("won the play-off final and goes up to");
        }
    }

    [Fact]
    public async Task A_play_off_holder_that_wins_the_final_stays_and_only_the_direct_places_move()
    {
        var (seasonId, countryId) = await SeasonAndFirstCountryAsync();

        await PublishSeasonAsync(seasonId);

        var playoff = await PlayPlayoffAsync(seasonId, countryId, decideFinal: true, challengerWins: false);
        var managerId = await AttachManagerAsync(playoff.Holder);

        SeasonRolloverResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);
        }

        result.Promotions.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces);
        result.Relegations.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectRelegationPlaces);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var nextSeasonId = result.NextSeasonId!.Value;

            (await NextLeagueClubsAsync(db, playoff.ParentDivisionId, nextSeasonId)).Should().HaveCount(WorldRuleSet.ClubsPerDivision)
                .And.Contain(playoff.Holder);
            (await NextLeagueClubsAsync(db, playoff.FeederDivisionId, nextSeasonId)).Should().HaveCount(WorldRuleSet.ClubsPerDivision)
                .And.Contain([playoff.Challenger, playoff.SemiLoser]);

            // The holder stayed up, so nobody tells its manager it moved.
            (await db.InboxMessages.CountAsync(candidate =>
                candidate.TemplateKey == InboxTemplates.SeasonMovement && candidate.RecipientManagerId == managerId)).Should().Be(0);
        }
    }

    [Fact]
    public async Task The_rollover_waits_while_a_play_off_tie_is_open_and_runs_once_it_is_decided()
    {
        var (seasonId, countryId) = await SeasonAndFirstCountryAsync();

        await PublishSeasonAsync(seasonId);

        var playoff = await PlayPlayoffAsync(seasonId, countryId, decideFinal: false, challengerWins: true);

        await using (var scope = _fixture.CreateScope())
        {
            await FluentActions
                .Awaiting(() => scope.ServiceProvider.GetRequiredService<RunSeasonRollover>().ExecuteAsync(seasonId, CancellationToken.None))
                .Should()
                .ThrowAsync<InvalidOperationException>("PR-4: an undecided tie is transient, like an unpublished round")
                .Where(exception => exception.GetType() == typeof(InvalidOperationException));

            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.SeasonRollovers.SingleAsync(row => row.SeasonId == seasonId)).Phase.Should().Be(SeasonRolloverPhase.Started);

            var plans = scope.ServiceProvider.GetRequiredService<IPlayoffRepository>();

            (await plans.CountUndecidedAsync(seasonId, CancellationToken.None)).Should().Be(1);
            (await plans.IsSeasonSettledAsync(seasonId, CancellationToken.None)).Should().BeFalse();

            var preview = await scope.ServiceProvider.GetRequiredService<PreviewSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);

            preview!.Ready.Should().BeFalse();
            preview.Preflight.UndecidedPlayoffTies.Should().Be(1);
            preview.Totals.Promotions.Should().Be(0, "the movement is only worked out once nothing is left to play");
        }

        await DecideFinalAsync(playoff, challengerWins: true);

        await using (var scope = _fixture.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RunSeasonRollover>()
                .ExecuteAsync(seasonId, CancellationToken.None);

            result.Outcome.Should().Be(SeasonRolloverOutcome.Completed);
            result.Promotions.Should().Be(LaunchCountries.All.Count * WorldRuleSet.DirectPromotionPlaces + 1);
        }
    }

    [Fact]
    public async Task The_scheduler_queues_the_rollover_only_once_every_round_is_published_and_every_tie_decided()
    {
        var (seasonId, countryId) = await SeasonAndFirstCountryAsync();

        var season = await WithDbAsync(db => db.Seasons.SingleAsync(candidate => candidate.Id == seasonId));

        // Past the season's last matchday and past the semi-finals' lock, so the catch-up draws nothing and only the gate is under test.
        var late = SeasonCalendar.PlayoffKickoffs(DateOnly.FromDateTime(season.StartsAt.UtcDateTime))[0].AddHours(1);

        _fixture.Clock.UtcNow = late;

        (await QueuedRolloversAsync(late)).Should().Be(0, "the season's rounds are not published yet");

        await PublishSeasonAsync(seasonId);

        var playoff = await PlayPlayoffAsync(seasonId, countryId, decideFinal: false, challengerWins: true);

        (await QueuedRolloversAsync(late)).Should().Be(0, "a play-off tie has no winner yet");

        await DecideFinalAsync(playoff, challengerWins: true);

        (await QueuedRolloversAsync(late)).Should().Be(1, "nothing is left to play");
        (await QueuedRolloversAsync(late)).Should().Be(1, "a repeated pass queues nothing more");
    }

    private async Task<int> QueuedRolloversAsync(DateTimeOffset now)
    {
        await using var scope = _fixture.CreateScope();

        // The scheduler is worker-only and internal, so the test builds it the way the worker's container would.
        var type = typeof(Tactical11ManagerDbContext).Assembly.GetType("Tactical11Manager.Infrastructure.Competition.SeasonRolloverScheduler")
            ?? throw new InvalidOperationException("The season-rollover scheduler type moved.");
        var scheduler = (IJobMaterializer)ActivatorUtilities.CreateInstance(scope.ServiceProvider, type);

        await scheduler.MaterializeAsync(now, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.Jobs.CountAsync(job => job.JobType == SeasonRolloverJobTypes.Rollover);
    }

    private async Task<T> WithDbAsync<T>(Func<Tactical11ManagerDbContext, Task<T>> read)
    {
        await using var scope = _fixture.CreateScope();

        return await read(scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>());
    }

    private async Task<(Guid SeasonId, Guid CountryId)> SeasonAndFirstCountryAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var seasonId = await db.Seasons
            .Where(candidate => candidate.WorldId == _fixture.WorldId)
            .Select(candidate => candidate.Id)
            .SingleAsync();
        var countryId = await db.Countries
            .OrderBy(country => country.SortOrder)
            .Select(country => country.Id)
            .FirstAsync();

        return (seasonId, countryId);
    }

    private static async Task<List<Guid>> NextLeagueClubsAsync(Tactical11ManagerDbContext db, Guid divisionId, Guid nextSeasonId) =>
        await (
            from entry in db.ClubSeasonEntries
            join divisionSeason in db.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            where divisionSeason.DivisionId == divisionId && divisionSeason.SeasonId == nextSeasonId
            select entry.ClubId)
            .ToListAsync();

    /// <summary>
    /// Plays one country's bracket the way the pipeline stores it, without the engine: the semi-final is won by the home club (the 3rd),
    /// and the final is won by the challenger or the holder as asked. The play-off rounds are published, so the table is untouched.
    /// </summary>
    private async Task<PlayoffPlayed> PlayPlayoffAsync(Guid seasonId, Guid countryId, bool decideFinal, bool challengerWins)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var season = await db.Seasons.SingleAsync(candidate => candidate.Id == seasonId);
        var divisions = await db.Divisions.Where(division => division.CountryId == countryId).ToListAsync();
        var parent = divisions.Single(division => division.TierNumber == 1);
        var feeder = divisions.Single(division => division.TierNumber == 2);

        var leagues = new List<LeagueStandings>();

        foreach (var division in divisions)
        {
            leagues.Add(new LeagueStandings(
                division.Id,
                division.TierNumber,
                division.LeagueNumber,
                division.ParentDivisionId,
                await RankedClubsAsync(scope, division.Id, seasonId)));
        }

        var pairings = PlayoffBracket.Draw(leagues);
        var semiPairing = pairings.Single(pairing => pairing.Stage == PlayoffStage.SemiFinal);
        var finalPairing = pairings.Single(pairing => pairing.Stage == PlayoffStage.Final);
        var kickoffs = SeasonCalendar.PlayoffKickoffs(DateOnly.FromDateTime(season.StartsAt.UtcDateTime));

        var divisionSeasons = await db.DivisionSeasons
            .Where(divisionSeason => divisionSeason.SeasonId == seasonId)
            .ToDictionaryAsync(divisionSeason => divisionSeason.DivisionId);

        var semi = PlayoffTie.Draw(Guid.CreateVersion7(), seasonId, countryId, semiPairing, now);
        var final = PlayoffTie.Draw(Guid.CreateVersion7(), seasonId, countryId, finalPairing, now);

        AddPlayedRound(db, semi, divisionSeasons[feeder.Id].Id, kickoffs[semi.Slot - 1], homeWins: true, now);
        semi.Decide(semi.HomeClubId, PlayoffDecision.Score, null, null, null, now);
        final.FillAway(semi.HomeClubId, feeder.Id, semi.HomePlace, now);
        db.PlayoffTies.AddRange(semi, final);

        AddPlayedRound(db, final, divisionSeasons[parent.Id].Id, kickoffs[final.Slot - 1], homeWins: !challengerWins, now);

        // A final that was played but whose tie was never decided is the state an operator is left with when progression failed.
        if (decideFinal)
        {
            final.Decide(challengerWins ? semi.HomeClubId : final.HomeClubId, PlayoffDecision.Score, null, null, null, now);
        }

        await db.SaveChangesAsync(CancellationToken.None);

        return new PlayoffPlayed(parent.Id, feeder.Id, final.HomeClubId, semi.HomeClubId, semi.AwayClubId!.Value, final.Id);
    }

    /// <summary>Gives a played final its winner, as an operator would once the cause was found.</summary>
    private async Task DecideFinalAsync(PlayoffPlayed playoff, bool challengerWins)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var final = await db.PlayoffTies.SingleAsync(tie => tie.Id == playoff.FinalTieId);

        final.Decide(challengerWins ? playoff.Challenger : playoff.Holder, PlayoffDecision.Score, null, null, null, now);

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static void AddPlayedRound(
        Tactical11ManagerDbContext db,
        PlayoffTie tie,
        Guid hostDivisionSeasonId,
        DateTimeOffset kickoff,
        bool homeWins,
        DateTimeOffset now)
    {
        var fixture = AddRound(db, tie, hostDivisionSeasonId, kickoff, now);
        var matchday = db.Matchdays.Local.Single(candidate => candidate.Id == fixture.MatchdayId);

        fixture.Lock(now);
        fixture.Stage(homeWins ? 1 : 0, homeWins ? 0 : 1, Guid.CreateVersion7(), now);
        fixture.Publish(now);
        matchday.MarkStaged(now);
        matchday.Publish(now);
    }

    private static Fixture AddRound(
        Tactical11ManagerDbContext db,
        PlayoffTie tie,
        Guid hostDivisionSeasonId,
        DateTimeOffset kickoff,
        DateTimeOffset now)
    {
        var matchday = Matchday.SchedulePlayoff(Guid.CreateVersion7(), hostDivisionSeasonId, tie.Slot, kickoff, now);
        var fixture = Fixture.Schedule(Guid.CreateVersion7(), matchday.Id, tie.HomeClubId, tie.AwayClubId!.Value, kickoff, now);

        db.Matchdays.Add(matchday);
        db.Fixtures.Add(fixture);
        tie.AttachFixture(fixture.Id, now);

        return fixture;
    }

    private sealed record PlayoffPlayed(
        Guid ParentDivisionId,
        Guid FeederDivisionId,
        Guid Holder,
        Guid Challenger,
        Guid SemiLoser,
        Guid FinalTieId);

    /// <summary>The total position awards every rank of a tier earns (`FIN-5`).</summary>
    private static long SumPositionAwards(int tier) =>
        Enumerable.Range(1, WorldRuleSet.ClubsPerDivision)
            .Sum(rank => EconomyRuleSet.PrizeMinorFor(tier, rank));

    /// <summary>
    /// Attaches a fresh manager to a club, so the rollover has someone to notify about its movement (`PR-1`).
    /// </summary>
    private async Task<Guid> AttachManagerAsync(Guid clubId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var userId = Guid.CreateVersion7();
        var suffix = userId.ToString("N");

        var user = User.Register(
            userId,
            $"{suffix}@example.com",
            $"Mover{suffix}"[..20],
            "hash",
            $"stamp-{suffix}",
            now);
        user.MarkEmailVerified(now);
        db.Users.Add(user);

        var managerId = Guid.CreateVersion7();
        db.Managers.Add(Manager.Create(managerId, userId, "en-GB", "Europe/London", now));

        db.ClubTenures.Add(ClubTenure.Start(
            Guid.CreateVersion7(),
            clubId,
            managerId,
            $"mover-{suffix}",
            now));

        await db.SaveChangesAsync(CancellationToken.None);

        return managerId;
    }

    /// <summary>Marks every matchday of a season published with a deterministic score and rebuilds the tables.</summary>
    private async Task PublishSeasonAsync(Guid seasonId)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = _fixture.Clock.UtcNow;

        var divisionSeasonIds = await db.DivisionSeasons
            .Where(divisionSeason => divisionSeason.SeasonId == seasonId)
            .Select(divisionSeason => divisionSeason.Id)
            .ToListAsync();

        foreach (var divisionSeasonId in divisionSeasonIds)
        {
            var matchdays = await db.Matchdays
                .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId)
                .ToListAsync();
            var matchdayIds = matchdays.Select(matchday => matchday.Id).ToList();

            var fixtures = await db.Fixtures
                .Where(fixture => matchdayIds.Contains(fixture.MatchdayId))
                .ToListAsync();

            foreach (var fixture in fixtures)
            {
                var key = fixture.Id.ToString("D");
                var home = (int)(DeterministicDigest.SeedOf(key, "home") % 4);
                var away = (int)(DeterministicDigest.SeedOf(key, "away") % 4);

                fixture.Lock(now);
                fixture.Stage(home, away, Guid.CreateVersion7(), now);
                fixture.Publish(now);
            }

            foreach (var matchday in matchdays)
            {
                matchday.MarkStaged(now);
                matchday.Publish(now);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);

        // The table is a projection of published results (TBL-13), so it is rebuilt from the fabricated ones
        // exactly as the live publication would, and preflight then reconciles.
        var rebuild = scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>();

        foreach (var divisionSeasonId in divisionSeasonIds)
        {
            var reconciled = await rebuild.ExecuteAsync(divisionSeasonId, apply: true, CancellationToken.None);

            reconciled.Outcome.Should().Be(ProjectionRebuildOutcome.Rebuilt);
        }
    }

    private static async Task<List<Guid>> RankedClubsAsync(
        AsyncServiceScope scope,
        Guid divisionId,
        Guid seasonId)
    {
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var divisionSeasonId = await db.DivisionSeasons
            .Where(divisionSeason => divisionSeason.DivisionId == divisionId && divisionSeason.SeasonId == seasonId)
            .Select(divisionSeason => divisionSeason.Id)
            .SingleAsync();

        return await db.Standings
            .Where(standing => standing.DivisionSeasonId == divisionSeasonId)
            .OrderBy(standing => standing.Rank)
            .Select(standing => standing.ClubId)
            .ToListAsync();
    }
}

/// <summary>The rollover tests' own seeded world.</summary>
public sealed class RolloverFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public RolloverFixture()
        : base("tactical11_rollover")
    {
    }
}

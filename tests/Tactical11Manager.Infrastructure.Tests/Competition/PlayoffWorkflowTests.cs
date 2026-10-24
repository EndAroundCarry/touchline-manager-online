using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Application.Competition.Playoffs;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Auth;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Match;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.Infrastructure.Tests.Competition;

/// <summary>The play-off workflow's own collection, so what it publishes, draws and moves the clock past cannot change another test's world.</summary>
[CollectionDefinition(Name)]
public sealed class PlayoffWorkflowCollection : ICollectionFixture<MatchdayFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "playoff-workflow";
}

/// <summary>
/// The promotion play-off pipeline against real PostgreSQL: the draw at the end of the league season, the two matches, and what each leaves
/// alone (`PR-4`, `PR-6`, ADR-0087).
/// </summary>
/// <remarks>
/// <para>
/// Every test plays the thirtieth round of both leagues of one country through the real lock, resolve and publish steps, which is what ends
/// the league season and draws the bracket; the other twenty-nine rounds are not played, because the draw reads only the table the last round
/// leaves and the pipeline does not care how the table came about. Each test takes a country of its own, so the order of the tests is not
/// something one may depend on.
/// </para>
/// <para>
/// The engine decides who wins, and that is not something a test can choose, so where a test needs a score (a win, or a draw that goes to
/// penalties) it sets the staged fixture's score between the resolve and the publication, which is the one place the result is still private.
/// The shoot-out is run from the stored match, so it is the real one.
/// </para>
/// </remarks>
[Collection(PlayoffWorkflowCollection.Name)]
public sealed class PlayoffWorkflowTests
{
    /// <summary>Initializes the tests.</summary>
    public PlayoffWorkflowTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests play in.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task The_draw_happens_on_the_second_thirtieth_matchday_of_the_pair_and_tells_the_clubs_in_it()
    {
        var pair = await FindPairAsync(LaunchCountries.SpainCode);

        await SetClockAfterLastRoundAsync(pair);

        var managers = await AppointManagersAsync([.. await ClubsOfAsync(pair.Upper), .. await ClubsOfAsync(pair.Lower)]);

        await PlayAsync(pair.Upper.LastRoundId);

        (await TiesAsync(pair)).Should().BeEmpty("only one of the two leagues has played its last round");

        await PlayAsync(pair.Lower.LastRoundId);

        var ties = await TiesAsync(pair);
        var upperTable = await RankedClubsAsync(pair.Upper);
        var lowerTable = await RankedClubsAsync(pair.Lower);

        ties.Select(tie => tie.Stage).Should().Equal(PlayoffStage.SemiFinal, PlayoffStage.Final);

        var semi = ties[0];
        var final = ties[1];

        semi.HomeClubId.Should().Be(lowerTable[2], "the lower league's 3rd hosts");
        semi.AwayClubId.Should().Be(lowerTable[3], "against its 4th");
        semi.HomePlace.Should().Be(WorldRuleSet.PlayoffSemiHomePlace);
        semi.AwayPlace.Should().Be(WorldRuleSet.PlayoffSemiAwayPlace);
        semi.Slot.Should().Be(WorldRuleSet.PlayoffSemiSlot);
        semi.ParentDivisionId.Should().Be(pair.Upper.DivisionId);
        semi.FeederDivisionId.Should().Be(pair.Lower.DivisionId);
        semi.FixtureId.Should().NotBeNull("the semi-final is scheduled the moment it is drawn");

        final.HomeClubId.Should().Be(upperTable[WorldRuleSet.PlayoffHolderPlace - 1], "the higher league's 14th waits in the final");
        final.HomeDivisionId.Should().Be(pair.Upper.DivisionId);
        final.AwayClubId.Should().BeNull("the away side is the semi-final's winner");
        final.FixtureId.Should().BeNull("the final is scheduled when the semi-final is decided");

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var round = await db.Matchdays.AsNoTracking().SingleAsync(matchday => matchday.Id == db.Fixtures
                .Where(fixture => fixture.Id == semi.FixtureId)
                .Select(fixture => fixture.MatchdayId)
                .Single());

            round.Kind.Should().Be(MatchdayKind.Playoff);
            round.RoundNumber.Should().Be(WorldRuleSet.MatchdaysPerSeason + WorldRuleSet.PlayoffSemiSlot);
            round.DivisionSeasonId.Should().Be(pair.Lower.DivisionSeasonId, "a semi-final is a round of the lower league");
            round.KickoffAt.Should().Be(pair.Kickoffs[WorldRuleSet.PlayoffSemiSlot - 1]);
            round.PublicationStatus.Should().Be(MatchdayPublicationStatus.Pending);

            var fixture = await db.Fixtures.AsNoTracking().SingleAsync(candidate => candidate.Id == semi.FixtureId);

            fixture.HomeClubId.Should().Be(semi.HomeClubId);
            fixture.AwayClubId.Should().Be(semi.AwayClubId!.Value);
            fixture.Status.Should().Be(FixtureStatus.Scheduled);

            (await db.NewsItems.CountAsync(item => item.TemplateKey == NewsTemplates.PlayoffDrawn && item.CountryId == pair.CountryId))
                .Should().Be(1, "the draw is news");

            var told = await db.InboxMessages
                .Where(message => message.TemplateKey == PlayoffInboxTemplates.Drawn && managers.Values.Contains(message.RecipientManagerId))
                .ToListAsync();

            told.Select(message => message.RecipientManagerId).Should().BeEquivalentTo(
                [managers[semi.HomeClubId], managers[semi.AwayClubId.Value], managers[final.HomeClubId]],
                "the 3rd, the 4th and the 14th are told, and nobody else");

            var home = told.Single(message => message.RecipientManagerId == managers[semi.HomeClubId]);
            var text = InboxMessageText.Render(home.TemplateKey, home.ParametersJson);

            text.Title.Should().Be("Play-off semi-final: you finished 3rd");
            text.Body.Should().Contain("You host").And.Contain("Kickoff is");
            home.RelatedEntityId.Should().Be(semi.FixtureId);
        }

        await using (var scope = Fixture.CreateScope())
        {
            var draw = scope.ServiceProvider.GetRequiredService<PlayoffDraw>();

            (await draw.CatchUpAsync(CancellationToken.None)).Should().Be(0, "a drawn pair is left alone");
            (await TiesAsync(pair)).Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task A_semi_final_decided_by_the_score_sets_the_final_and_leaves_the_league_alone()
    {
        var pair = await FindPairAsync(LaunchCountries.GermanyCode);

        await SetClockAfterLastRoundAsync(pair);
        await PlayAsync(pair.Upper.LastRoundId);
        await PlayAsync(pair.Lower.LastRoundId);

        var semi = (await TiesAsync(pair)).Single(tie => tie.Stage == PlayoffStage.SemiFinal);
        var holder = (await TiesAsync(pair)).Single(tie => tie.Stage == PlayoffStage.Final).HomeClubId;
        var managers = await AppointManagersAsync([semi.HomeClubId, semi.AwayClubId!.Value, holder]);
        var semiRoundId = await RoundOfFixtureAsync(semi.FixtureId!.Value);

        var lowerBefore = await TableOfAsync(pair.Lower);
        var upperBefore = await TableOfAsync(pair.Upper);
        var statsBefore = await StatisticsCountAsync(pair.Lower);

        await LockAndResolveAsync(semiRoundId);
        await SetScoreAsync(semi.FixtureId.Value, 2, 1);

        var published = await PublishAsync(semiRoundId);

        published.Outcome.Should().Be(PublishMatchdayOutcome.Published);

        var ties = await TiesAsync(pair);
        var decided = ties.Single(tie => tie.Stage == PlayoffStage.SemiFinal);
        var final = ties.Single(tie => tie.Stage == PlayoffStage.Final);

        decided.WinnerClubId.Should().Be(semi.HomeClubId);
        decided.LoserClubId.Should().Be(semi.AwayClubId);
        decided.DecidedBy.Should().Be(PlayoffDecision.Score);
        decided.ShootoutHomeScore.Should().BeNull();

        final.AwayClubId.Should().Be(semi.HomeClubId, "the winner fills the waiting away side");
        final.AwayDivisionId.Should().Be(pair.Lower.DivisionId);
        final.AwayPlace.Should().Be(WorldRuleSet.PlayoffSemiHomePlace);
        final.FixtureId.Should().NotBeNull();

        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var finalFixture = await db.Fixtures.AsNoTracking().SingleAsync(fixture => fixture.Id == final.FixtureId);
        var finalRound = await db.Matchdays.AsNoTracking().SingleAsync(matchday => matchday.Id == finalFixture.MatchdayId);

        finalFixture.HomeClubId.Should().Be(holder, "the 14th is at home in the final");
        finalFixture.AwayClubId.Should().Be(semi.HomeClubId);
        finalRound.Kind.Should().Be(MatchdayKind.Playoff);
        finalRound.RoundNumber.Should().Be(WorldRuleSet.MatchdaysPerSeason + WorldRuleSet.PlayoffFinalSlot);
        finalRound.DivisionSeasonId.Should().Be(pair.Upper.DivisionSeasonId, "a final is a round of the higher league");
        finalRound.KickoffAt.Should().Be(pair.Kickoffs[WorldRuleSet.PlayoffFinalSlot - 1]);

        // The league is untouched: the table, the season statistics, and the reconciliation that checks both.
        (await TableOfAsync(pair.Lower)).Should().BeEquivalentTo(lowerBefore, options => options.WithStrictOrdering());
        (await TableOfAsync(pair.Upper)).Should().BeEquivalentTo(upperBefore, options => options.WithStrictOrdering());
        (await StatisticsCountAsync(pair.Lower)).Should().Be(statsBefore, "a play-off match is outside the league's player statistics");

        foreach (var divisionSeasonId in new[] { pair.Lower.DivisionSeasonId, pair.Upper.DivisionSeasonId })
        {
            var rebuild = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
                .ExecuteAsync(divisionSeasonId, apply: false, CancellationToken.None);

            rebuild.Outcome.Should().Be(ProjectionRebuildOutcome.Reconciled);
            rebuild.HasDrift.Should().BeFalse();
        }

        // The money: the gate to the host, and nothing the league pays in thirty equal parts.
        var semiFixtureId = semi.FixtureId.Value;
        var takings = await db.LedgerEntries.Where(entry => entry.SourceId == semiFixtureId).ToListAsync();

        takings.Should().NotBeEmpty("the host's gate is posted");
        takings.Should().OnlyContain(entry => entry.ClubId == semi.HomeClubId, "only the host takes the gate and the snack bar");
        takings.Should().Contain(entry => entry.Category == LedgerCategory.GateReceipt);
        (await db.LedgerEntries.CountAsync(entry => entry.SourceId == semiRoundId))
            .Should().Be(0, "no television money, sponsor money or wages are posted for a play-off round");

        // Both clubs' fans heard it, each at the tier of its own league.
        (await db.ClubFanEvents.CountAsync(fanEvent => fanEvent.FixtureId == semiFixtureId && fanEvent.Kind == FanEventKind.Match))
            .Should().Be(2);

        // The managers are told, and what the result means waits behind the same gate as the score.
        var winnerResult = await InboxOfAsync(managers[semi.HomeClubId], InboxTemplates.ResultRecorded);
        var winnerText = InboxMessageText.Render(winnerResult.TemplateKey, winnerResult.ParametersJson);

        winnerText.Title.Should().StartWith("Play-off semi-final: At home to");
        winnerText.Body.Should().NotContain("2").And.NotContain("through");
        winnerText.Spoiler.Should().Contain("Won 2–1").And.Contain("You go through to the play-off final.");

        var loserResult = await InboxOfAsync(managers[semi.AwayClubId.Value], InboxTemplates.ResultRecorded);

        InboxMessageText.Render(loserResult.TemplateKey, loserResult.ParametersJson).Spoiler
            .Should().Contain("Lost 1–2").And.Contain("You are out of the play-off.");

        (await db.InboxMessages.AnyAsync(message => message.RecipientManagerId == managers[holder]
                && message.TemplateKey == PlayoffInboxTemplates.FinalSet))
            .Should().BeTrue("the 14th is told its final is set, without the opponent in the headline");

        (await db.InboxMessages.CountAsync(message => message.TemplateKey == InboxTemplates.TableMoved
                && message.RecipientManagerId == managers[semi.HomeClubId] && message.RelatedEntityId == semiFixtureId))
            .Should().Be(0, "a play-off match has no table position");
    }

    [Fact]
    public async Task A_semi_final_that_ends_level_is_decided_on_penalties_from_the_stored_match()
    {
        var pair = await FindPairAsync(LaunchCountries.ItalyCode);

        await SetClockAfterLastRoundAsync(pair);
        await PlayAsync(pair.Upper.LastRoundId);
        await PlayAsync(pair.Lower.LastRoundId);

        var semi = (await TiesAsync(pair)).Single(tie => tie.Stage == PlayoffStage.SemiFinal);
        var managers = await AppointManagersAsync([semi.HomeClubId, semi.AwayClubId!.Value]);
        var semiRoundId = await RoundOfFixtureAsync(semi.FixtureId!.Value);

        await LockAndResolveAsync(semiRoundId);
        await SetScoreAsync(semi.FixtureId.Value, 1, 1);
        await PublishAsync(semiRoundId);

        var ties = await TiesAsync(pair);
        var decided = ties.Single(tie => tie.Stage == PlayoffStage.SemiFinal);

        decided.DecidedBy.Should().Be(PlayoffDecision.Penalties);
        decided.ShootoutHomeScore.Should().NotBe(decided.ShootoutAwayScore, "a shoot-out has a winner");
        decided.WinnerClubId.Should().Be(
            decided.ShootoutHomeScore > decided.ShootoutAwayScore ? semi.HomeClubId : semi.AwayClubId,
            "the shoot-out decides it");

        var shootout = PlayoffShootoutDocument.Read(decided.ShootoutKicks!);

        shootout.Version.Should().Be("shootout-v1");
        shootout.Kicks.Count.Should().BeGreaterThanOrEqualTo(6, "the earliest a shoot-out ends is the sixth kick");
        shootout.Kicks.Count(kick => kick.Side == "home" && kick.Outcome == "scored").Should().Be(decided.ShootoutHomeScore);
        shootout.Kicks.Count(kick => kick.Side == "away" && kick.Outcome == "scored").Should().Be(decided.ShootoutAwayScore);

        var final = ties.Single(tie => tie.Stage == PlayoffStage.Final);

        final.AwayClubId.Should().Be(decided.WinnerClubId, "the shoot-out's winner plays the final");

        var winnerClub = decided.WinnerClubId!.Value;
        var message = await InboxOfAsync(managers[winnerClub], InboxTemplates.ResultRecorded);
        var text = InboxMessageText.Render(message.TemplateKey, message.ParametersJson);

        text.Spoiler.Should().Contain("after 90 minutes").And.Contain("on penalties");
    }

    [Fact]
    public async Task A_red_card_in_the_semi_final_is_served_in_the_final_and_a_visitors_cards_go_to_its_own_league()
    {
        var pair = await FindPairAsync(LaunchCountries.PortugalCode);

        await SetClockAfterLastRoundAsync(pair);
        await PlayAsync(pair.Upper.LastRoundId);
        await PlayAsync(pair.Lower.LastRoundId);

        var semi = (await TiesAsync(pair)).Single(tie => tie.Stage == PlayoffStage.SemiFinal);
        var holder = (await TiesAsync(pair)).Single(tie => tie.Stage == PlayoffStage.Final).HomeClubId;
        var semiRoundId = await RoundOfFixtureAsync(semi.FixtureId!.Value);

        await LockAndResolveAsync(semiRoundId);
        await SetScoreAsync(semi.FixtureId.Value, 2, 1);

        var sentOffInSemi = await PickPlayerAsync(semi.FixtureId.Value, semi.HomeClubId);

        await InjectAsync(semi.FixtureId.Value, semi.HomeClubId, sentOffInSemi, MatchEventType.RedCard);
        await PublishAsync(semiRoundId);

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var suspension = await db.PlayerUnavailabilities.SingleAsync(record =>
                record.PlayerId == sentOffInSemi && record.Type == UnavailabilityType.Suspension);

            suspension.ClubId.Should().Be(semi.HomeClubId);
            suspension.RemainingFixtures.Should().Be(WorldRuleSet.RedCardSuspensionFixtures);
            suspension.ResolvedAt.Should().BeNull("the match that caused it does not serve it");
        }

        var final = (await TiesAsync(pair)).Single(tie => tie.Stage == PlayoffStage.Final);
        var finalRoundId = await RoundOfFixtureAsync(final.FixtureId!.Value);

        await LockAndResolveAsync(finalRoundId);

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var snapshot = await db.InputSnapshots.AsNoTracking().SingleAsync(candidate => candidate.FixtureId == final.FixtureId);
            var input = MatchSnapshotDocument.Read(snapshot.SnapshotJson).Input;

            input.Away.Squad.Select(player => player.ParticipantId).Should().NotContain(
                sentOffInSemi,
                "the player sent off in the semi-final is not picked for the final (DIS-5)");
        }

        // The final's visitor is a club of the lower league, in the higher league's division-season: its cards belong to its own.
        var sentOffInFinal = await PickPlayerAsync(final.FixtureId.Value, semi.HomeClubId, excluding: sentOffInSemi);

        await InjectAsync(final.FixtureId.Value, semi.HomeClubId, sentOffInFinal, MatchEventType.RedCard);
        await SetScoreAsync(final.FixtureId.Value, 0, 1);
        await PublishAsync(finalRoundId);

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var served = await db.PlayerUnavailabilities.SingleAsync(record =>
                record.PlayerId == sentOffInSemi && record.Type == UnavailabilityType.Suspension);

            served.RemainingFixtures.Should().Be(0);
            served.ResolvedAt.Should().NotBeNull("the final is the next fixture the club plays, so it served the ban");

            var record = await db.DisciplineRecords.SingleAsync(candidate => candidate.PlayerId == sentOffInFinal);

            record.DivisionSeasonId.Should().Be(pair.Lower.DivisionSeasonId, "the visitor's league, not the league that hosted the match");
            record.RedCards.Should().Be(1);

            (await db.PlayerUnavailabilities.AnyAsync(candidate => candidate.PlayerId == sentOffInFinal
                    && candidate.ClubId == semi.HomeClubId && candidate.Type == UnavailabilityType.Suspension))
                .Should().BeTrue("the ban is the club's, wherever the match was");

            var decided = await db.PlayoffTies.AsNoTracking().SingleAsync(tie => tie.Id == final.Id);

            decided.WinnerClubId.Should().Be(semi.HomeClubId, "the challenger won the final");
            decided.LoserClubId.Should().Be(holder);
            decided.DecidedBy.Should().Be(PlayoffDecision.Score);

            var finalFixtureId = final.FixtureId.Value;
            var gate = await db.LedgerEntries.Where(entry => entry.SourceId == finalFixtureId).ToListAsync();

            gate.Should().NotBeEmpty();
            gate.Should().OnlyContain(entry => entry.ClubId == holder, "the final's gate is the host's, in the higher league");

            (await db.ClubFanEvents.CountAsync(fanEvent => fanEvent.FixtureId == finalFixtureId && fanEvent.Kind == FanEventKind.Match))
                .Should().Be(2);

            (await db.Matchdays.CountAsync(matchday => matchday.Kind == MatchdayKind.Playoff
                    && (matchday.DivisionSeasonId == pair.Upper.DivisionSeasonId || matchday.DivisionSeasonId == pair.Lower.DivisionSeasonId)))
                .Should().Be(2, "a semi-final and a final, and nothing after the final");
        }
    }

    [Fact]
    public async Task Publishing_a_play_off_round_again_changes_nothing()
    {
        var pair = await FindPairAsync(LaunchCountries.RomaniaCode);

        await SetClockAfterLastRoundAsync(pair);
        await PlayAsync(pair.Upper.LastRoundId);
        await PlayAsync(pair.Lower.LastRoundId);

        var semi = (await TiesAsync(pair)).Single(tie => tie.Stage == PlayoffStage.SemiFinal);
        var managers = await AppointManagersAsync([semi.HomeClubId, semi.AwayClubId!.Value]);
        var semiRoundId = await RoundOfFixtureAsync(semi.FixtureId!.Value);

        await LockAndResolveAsync(semiRoundId);
        await SetScoreAsync(semi.FixtureId.Value, 0, 3);

        (await PublishAsync(semiRoundId)).Outcome.Should().Be(PublishMatchdayOutcome.Published);

        var before = await CountsAsync(pair, managers.Values);

        (await PublishAsync(semiRoundId)).Outcome.Should().Be(PublishMatchdayOutcome.AlreadyPublished);

        (await CountsAsync(pair, managers.Values)).Should().Be(before);

        var ties = await TiesAsync(pair);

        ties.Single(tie => tie.Stage == PlayoffStage.SemiFinal).WinnerClubId.Should().Be(semi.AwayClubId, "the visitor won 3–0");
        ties.Single(tie => tie.Stage == PlayoffStage.Final).AwayClubId.Should().Be(semi.AwayClubId);
    }

    [Fact]
    public async Task A_pair_whose_league_season_ends_after_the_semi_finals_lock_plays_no_play_off_until_a_catch_up_finds_time()
    {
        var pair = await FindPairAsync(LaunchCountries.BelgiumCode);

        // The semi-final's team sheets have locked by the time the last rounds publish: there is no semi-final to schedule (a late pair).
        Fixture.Clock.UtcNow = LockOf(pair.Kickoffs, WorldRuleSet.PlayoffSemiSlot).AddHours(1);

        await PlayAsync(pair.Upper.LastRoundId);
        await PlayAsync(pair.Lower.LastRoundId);

        (await TiesAsync(pair)).Should().BeEmpty("there was no time for a play-off");

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.Matchdays.CountAsync(matchday => matchday.Kind == MatchdayKind.Playoff
                    && (matchday.DivisionSeasonId == pair.Upper.DivisionSeasonId || matchday.DivisionSeasonId == pair.Lower.DivisionSeasonId)))
                .Should().Be(0);
        }

        // Back inside the window, the catch-up draws what the pair is owed, once.
        await SetClockAfterLastRoundAsync(pair);

        await using (var scope = Fixture.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PlayoffDraw>().CatchUpAsync(CancellationToken.None))
                .Should().BeGreaterThanOrEqualTo(1);
        }

        (await TiesAsync(pair)).Should().HaveCount(2);

        await using (var scope = Fixture.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PlayoffDraw>().CatchUpAsync(CancellationToken.None)).Should().Be(0);
        }

        (await TiesAsync(pair)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_final_whose_slot_has_locked_takes_the_next_slot_and_with_none_left_waits_for_an_operator()
    {
        var late = await FindPairAsync(LaunchCountries.EnglandCode);
        var tooLate = await FindPairAsync(LaunchCountries.NetherlandsCode);

        foreach (var (pair, clock, expectedRound) in new[]
        {
            (late, LockOf(late.Kickoffs, WorldRuleSet.PlayoffFinalSlot).AddHours(1), WorldRuleSet.MatchdaysPerSeason + 3),
            (tooLate, LockOf(tooLate.Kickoffs, WorldRuleSet.PlayoffSlots).AddHours(1), 0),
        })
        {
            await SetClockAfterLastRoundAsync(pair);
            await PlayAsync(pair.Upper.LastRoundId);
            await PlayAsync(pair.Lower.LastRoundId);

            var semi = (await TiesAsync(pair)).Single(tie => tie.Stage == PlayoffStage.SemiFinal);
            var semiRoundId = await RoundOfFixtureAsync(semi.FixtureId!.Value);

            await LockAndResolveAsync(semiRoundId);
            await SetScoreAsync(semi.FixtureId.Value, 2, 0);

            // The semi-final is published late, after the final's slot has locked.
            Fixture.Clock.UtcNow = clock;
            await PublishAsync(semiRoundId);

            var ties = await TiesAsync(pair);
            var final = ties.Single(tie => tie.Stage == PlayoffStage.Final);

            ties.Single(tie => tie.Stage == PlayoffStage.SemiFinal).IsDecided.Should().BeTrue("the semi-final is decided either way");
            final.AwayClubId.Should().Be(semi.HomeClubId, "the clubs of the final are known either way");

            if (expectedRound == 0)
            {
                final.FixtureId.Should().BeNull("with no slot left the final waits for an operator");
                final.IsDecided.Should().BeFalse();

                await using var scope = Fixture.CreateScope();
                var settled = await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Competition.IPlayoffRepository>()
                    .CountUndecidedAsync(pair.SeasonId, CancellationToken.None);

                settled.Should().BeGreaterThanOrEqualTo(1, "the season stays unsettled until the final is played");

                continue;
            }

            await using var readScope = Fixture.CreateScope();
            var db = readScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var round = await db.Matchdays.AsNoTracking().SingleAsync(matchday => matchday.Id == db.Fixtures
                .Where(fixture => fixture.Id == final.FixtureId)
                .Select(fixture => fixture.MatchdayId)
                .Single());

            round.RoundNumber.Should().Be(expectedRound, "the last slot is the only one left");
            round.KickoffAt.Should().Be(pair.Kickoffs[WorldRuleSet.PlayoffSlots - 1]);
        }
    }

    /// <summary>The instant a play-off slot's team sheets lock.</summary>
    private static DateTimeOffset LockOf(IReadOnlyList<DateTimeOffset> kickoffs, int slot) =>
        kickoffs[slot - 1].AddMinutes(-WorldRuleSet.TeamSheetLockMinutes);

    private async Task<Pair> FindPairAsync(string countryCode)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var world = await db.GameWorlds.OrderBy(candidate => candidate.CreatedAt).FirstAsync();
        var season = await db.Seasons.AsNoTracking()
            .SingleAsync(candidate => candidate.WorldId == world.Id && candidate.SequenceNumber == world.CurrentSeasonNumber);

        var divisions = await (
            from division in db.Divisions.AsNoTracking()
            join country in db.Countries on division.CountryId equals country.Id
            where country.Code == countryCode
            orderby division.TierNumber, division.LeagueNumber
            select division)
            .ToListAsync();

        var upper = divisions.First(division => division.TierNumber == 1);
        var lower = divisions.First(division => division.TierNumber == 2);

        lower.ParentDivisionId.Should().Be(upper.Id, "the second league feeds the first");

        var first = DateOnly.FromDateTime(season.StartsAt.UtcDateTime);

        return new Pair(
            season.Id,
            upper.CountryId,
            await LeagueAsync(db, upper, season.Id),
            await LeagueAsync(db, lower, season.Id),
            SeasonCalendar.PlayoffKickoffs(first),
            SeasonCalendar.KickoffAt(SeasonCalendar.MatchdayDates(first, WorldRuleSet.MatchdaysPerSeason)[^1]));
    }

    private static async Task<League> LeagueAsync(Tactical11ManagerDbContext db, Division division, Guid seasonId)
    {
        var divisionSeasonId = await db.DivisionSeasons
            .Where(candidate => candidate.DivisionId == division.Id && candidate.SeasonId == seasonId)
            .Select(candidate => candidate.Id)
            .SingleAsync();

        var lastRoundId = await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId
                && matchday.Kind == MatchdayKind.League
                && matchday.RoundNumber == WorldRuleSet.MatchdaysPerSeason)
            .Select(matchday => matchday.Id)
            .SingleAsync();

        return new League(division.Id, divisionSeasonId, lastRoundId);
    }

    /// <summary>Puts the clock just after the league season's last matchday, well before the semi-final locks.</summary>
    private Task SetClockAfterLastRoundAsync(Pair pair)
    {
        Fixture.Clock.UtcNow = pair.LastRoundKickoff.AddHours(2);

        return Task.CompletedTask;
    }

    private async Task PlayAsync(Guid matchdayId)
    {
        await LockAndResolveAsync(matchdayId);

        (await PublishAsync(matchdayId)).Outcome.Should().Be(PublishMatchdayOutcome.Published);
    }

    private async Task LockAndResolveAsync(Guid matchdayId)
    {
        await using var scope = Fixture.CreateScope();

        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>().ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);
    }

    private async Task<PublishMatchdayResult> PublishAsync(Guid matchdayId)
    {
        await using var scope = Fixture.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<PublishMatchday>().ExecuteAsync(matchdayId, CancellationToken.None);
    }

    private async Task<List<PlayoffTie>> TiesAsync(Pair pair)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.PlayoffTies
            .AsNoTracking()
            .Where(tie => tie.SeasonId == pair.SeasonId && tie.CountryId == pair.CountryId)
            .OrderBy(tie => tie.Slot)
            .ToListAsync();
    }

    private async Task<List<Guid>> RankedClubsAsync(League league)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.Standings
            .Where(standing => standing.DivisionSeasonId == league.DivisionSeasonId)
            .OrderBy(standing => standing.Rank)
            .Select(standing => standing.ClubId)
            .ToListAsync();
    }

    private async Task<List<(Guid ClubId, int Rank, int Played, int Points)>> TableOfAsync(League league)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var rows = await db.Standings
            .Where(standing => standing.DivisionSeasonId == league.DivisionSeasonId)
            .OrderBy(standing => standing.Rank)
            .Select(standing => new { standing.ClubId, standing.Rank, standing.Played, standing.Points })
            .ToListAsync();

        return [.. rows.Select(row => (row.ClubId, row.Rank, row.Played, row.Points))];
    }

    private async Task<int> StatisticsCountAsync(League league)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.PlayerSeasonStats.CountAsync(stat => stat.DivisionSeasonId == league.DivisionSeasonId);
    }

    private async Task<List<Guid>> ClubsOfAsync(League league)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.ClubSeasonEntries
            .Where(entry => entry.DivisionSeasonId == league.DivisionSeasonId)
            .Select(entry => entry.ClubId)
            .ToListAsync();
    }

    private async Task<Guid> RoundOfFixtureAsync(Guid fixtureId)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.Fixtures.Where(fixture => fixture.Id == fixtureId).Select(fixture => fixture.MatchdayId).SingleAsync();
    }

    /// <summary>Sets a staged fixture's score, the one thing a test cannot ask the engine for.</summary>
    private async Task SetScoreAsync(Guid fixtureId, int home, int away)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"update competition.fixtures set home_score = {home}, away_score = {away} where id = {fixtureId} and status = 'staged'");

        rows.Should().Be(1, "only a staged fixture's score is still private");
    }

    /// <summary>Appoints a manager to each club, so the club's messages have somewhere to go.</summary>
    private async Task<Dictionary<Guid, Guid>> AppointManagersAsync(IEnumerable<Guid> clubIds)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = Fixture.Clock.UtcNow;
        var managers = new Dictionary<Guid, Guid>();

        foreach (var clubId in clubIds.Distinct())
        {
            var suffix = Guid.NewGuid().ToString("N");
            var userId = Guid.CreateVersion7();

            db.Users.Add(User.Register(userId, $"playoff-{suffix}@example.com", $"Playoff {suffix}"[..24], "hash", "stamp", now));

            var manager = Manager.Create(Guid.CreateVersion7(), userId, "en-GB", "Europe/London", now);

            db.Managers.Add(manager);
            db.ClubTenures.Add(ClubTenure.Start(Guid.CreateVersion7(), clubId, manager.Id, $"playoff-test-{suffix}", now));

            managers[clubId] = manager.Id;
        }

        await db.SaveChangesAsync(CancellationToken.None);

        return managers;
    }

    private async Task<Domain.Comms.InboxMessage> InboxOfAsync(Guid managerId, string templateKey)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.InboxMessages
            .AsNoTracking()
            .Where(message => message.RecipientManagerId == managerId && message.TemplateKey == templateKey)
            .OrderByDescending(message => message.CreatedAt)
            .FirstAsync();
    }

    /// <summary>Counts what a republication must not add to: money, messages, fan history, ties and rounds.</summary>
    private async Task<(int Ledger, int Inbox, int Fans, long TieVersions, int Rounds)> CountsAsync(Pair pair, IEnumerable<Guid> managerIds)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var managers = managerIds.ToList();

        return (
            await db.LedgerEntries.CountAsync(),
            await db.InboxMessages.CountAsync(message => managers.Contains(message.RecipientManagerId)),
            await db.ClubFanEvents.CountAsync(),
            await db.PlayoffTies.Where(tie => tie.SeasonId == pair.SeasonId && tie.CountryId == pair.CountryId).SumAsync(tie => tie.Version),
            await db.Matchdays.CountAsync(matchday => matchday.Kind == MatchdayKind.Playoff));
    }

    /// <summary>Picks an outfield player of a club who played the fixture, and has no card or injury in it.</summary>
    private async Task<Guid> PickPlayerAsync(Guid fixtureId, Guid clubId, Guid? excluding = null)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var snapshot = await db.InputSnapshots.AsNoTracking().SingleAsync(candidate => candidate.FixtureId == fixtureId);
        var input = MatchSnapshotDocument.Read(snapshot.SnapshotJson).Input;
        var side = input.Home.ClubId == clubId ? input.Home : input.Away;

        var matchId = await db.Fixtures.Where(fixture => fixture.Id == fixtureId).Select(fixture => fixture.MatchId!.Value).SingleAsync();

        var affected = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId
                && matchEvent.ParticipantId != null
                && (matchEvent.Type == MatchEventType.YellowCard
                    || matchEvent.Type == MatchEventType.SecondYellowCard
                    || matchEvent.Type == MatchEventType.RedCard
                    || matchEvent.Type == MatchEventType.Injury))
            .Select(matchEvent => matchEvent.ParticipantId!.Value)
            .Distinct()
            .ToListAsync();

        return side.Squad
            .Where(participant => !participant.IsGoalkeeper
                && participant.ParticipantId != excluding
                && !affected.Contains(participant.ParticipantId))
            .OrderBy(participant => participant.ParticipantId)
            .First()
            .ParticipantId;
    }

    /// <summary>Adds a card to a simulated match's event stream, so the publication has an effect to apply.</summary>
    private async Task InjectAsync(Guid fixtureId, Guid clubId, Guid playerId, MatchEventType type)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var matchId = await db.Fixtures.Where(fixture => fixture.Id == fixtureId).Select(fixture => fixture.MatchId!.Value).SingleAsync();
        var sequence = await db.MatchEvents.Where(matchEvent => matchEvent.MatchId == matchId).MaxAsync(matchEvent => matchEvent.Sequence) + 1;

        db.MatchEvents.Add(MatchEvent.Record(
            Guid.CreateVersion7(),
            matchId,
            sequence,
            minute: 90,
            stoppageMinute: 0,
            clubId,
            type,
            participantId: playerId));

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private sealed record League(Guid DivisionId, Guid DivisionSeasonId, Guid LastRoundId);

    private sealed record Pair(
        Guid SeasonId,
        Guid CountryId,
        League Upper,
        League Lower,
        IReadOnlyList<DateTimeOffset> Kickoffs,
        DateTimeOffset LastRoundKickoff);
}

using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Tests.Competition;

/// <summary>
/// What the reads say about a promotion play-off: the table's zones, the stage of a match, and the shoot-out held back until the match is
/// published (`PR-1`, `PR-4`, `PR-6`).
/// </summary>
/// <remarks>
/// The zones come from the domain's own rule, so the table cannot name a place the rollover treats differently; the gate is the one thing
/// between a staged result and a manager, and a level 90 minutes is itself a spoiler.
/// </remarks>
public sealed class PlayoffMappingTests
{
    private static readonly Guid HomeClubId = Guid.CreateVersion7();
    private static readonly Guid AwayClubId = Guid.CreateVersion7();

    [Theory]
    [InlineData(true, false, "promotion", "promotion", "playoff", "playoff", "none", "none", "none")]
    [InlineData(false, true, "none", "none", "none", "none", "playoff", "relegation", "relegation")]
    [InlineData(true, true, "promotion", "promotion", "playoff", "playoff", "playoff", "relegation", "relegation")]
    [InlineData(false, false, "none", "none", "none", "none", "none", "none", "none")]
    public void A_table_names_the_zones_of_its_places_from_the_leagues_around_it(
        bool hasParent,
        bool hasFeeder,
        string first,
        string second,
        string third,
        string fourth,
        string fourteenth,
        string fifteenth,
        string sixteenth)
    {
        var rows = Enumerable.Range(1, WorldRuleSet.ClubsPerDivision)
            .Select(rank => Row(rank).ToResponse(hasParent, hasFeeder))
            .ToList();

        // The middle league has a play-off place at both ends, and the 14th is the only one of them in the lower half.
        var places = new[] { 1, 2, 3, 4, WorldRuleSet.PlayoffHolderPlace, 15, 16 };

        rows.Where(row => places.Contains(row.Rank)).Select(row => row.Zone).Should().Equal(
            [first, second, third, fourth, fourteenth, fifteenth, sixteenth]);
    }

    [Fact]
    public void A_zone_is_said_in_words_and_a_place_that_leads_nowhere_says_nothing()
    {
        var rows = Enumerable.Range(1, WorldRuleSet.ClubsPerDivision)
            .Select(rank => Row(rank).ToResponse(hasParent: true, hasFeeder: true))
            .ToList();

        rows[0].ZoneLabel.Should().Be("Promotion");
        rows[2].ZoneLabel.Should().Be("Play-off");
        rows[15].ZoneLabel.Should().Be("Relegation");
        rows[7].Zone.Should().Be("none");
        rows[7].ZoneLabel.Should().BeEmpty();
    }

    [Fact]
    public void A_table_row_outside_the_division_size_has_no_zone_rather_than_failing()
    {
        Row(0).ToResponse(hasParent: true, hasFeeder: true).Zone.Should().Be("none");
        Row(WorldRuleSet.ClubsPerDivision + 1).ToResponse(hasParent: true, hasFeeder: true).Zone.Should().Be("none");
    }

    [Fact]
    public void A_fixture_is_a_league_match_unless_it_belongs_to_a_play_off_tie()
    {
        var league = Fixture(stage: null).ToResponse(Guid.CreateVersion7(), 5);
        var semi = Fixture(PlayoffStage.SemiFinal).ToResponse(Guid.CreateVersion7(), 31);
        var final = Fixture(PlayoffStage.Final).ToResponse(Guid.CreateVersion7(), 32);

        (league.Kind, league.StageLabel).Should().Be(("league", null));
        (semi.Kind, semi.StageLabel).Should().Be(("playoff", "Play-off semi-final"));
        (final.Kind, final.StageLabel).Should().Be(("playoff", "Play-off final"));
    }

    [Fact]
    public void A_round_is_named_for_the_stage_of_its_match()
    {
        var round = new FixtureMatchdayRow(
            Guid.CreateVersion7(),
            WorldRuleSet.MatchdaysPerSeason + 1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            MatchdayPublicationStatus.Pending,
            [Fixture(PlayoffStage.SemiFinal)],
            MatchdayKind.Playoff);

        var response = round.ToResponse();

        response.Kind.Should().Be("playoff");
        response.StageLabel.Should().Be("Play-off semi-final");
    }

    [Fact]
    public void The_shoot_out_waits_for_the_match_to_be_published()
    {
        var tie = Shootout();

        foreach (var status in new[] { FixtureStatus.Scheduled, FixtureStatus.Locked, FixtureStatus.Simulating, FixtureStatus.Staged })
        {
            tie.ToResponse(status).Should().BeNull($"a {status} match has no public result (MAT-7)");
        }

        tie.ToResponse(FixtureStatus.Published).Should().NotBeNull();
    }

    [Fact]
    public void A_league_match_has_no_play_off_block()
    {
        ((PlayoffMatchRow?)null).ToResponse(FixtureStatus.Published).Should().BeNull();
    }

    [Fact]
    public void A_published_shoot_out_carries_its_score_its_winner_and_its_kicks_in_order()
    {
        var block = Shootout().ToResponse(FixtureStatus.Published)!;

        block.Stage.Should().Be("final");
        block.StageLabel.Should().Be("Play-off final");
        block.DecidedBy.Should().Be("penalties");
        block.WinnerClubId.Should().Be(AwayClubId);
        block.ShootoutHomeScore.Should().Be(3);
        block.ShootoutAwayScore.Should().Be(4);
        block.FirstSide.Should().Be("home");
        block.Kicks.Select(kick => (kick.Number, kick.Side, kick.TakerName, kick.Outcome)).Should().Equal(
            (1, "home", "Home Taker", "scored"),
            (2, "away", "Away Taker", "saved"));
        block.Kicks[1].GoalkeeperName.Should().Be("Home Keeper");
    }

    [Fact]
    public void A_tie_decided_on_the_score_has_no_kicks()
    {
        var block = new PlayoffMatchRow(PlayoffStage.SemiFinal, PlayoffDecision.Score, HomeClubId, null, null, null, [])
            .ToResponse(FixtureStatus.Published)!;

        block.DecidedBy.Should().Be("score");
        block.Kicks.Should().BeEmpty();
        block.ShootoutHomeScore.Should().BeNull();
    }

    private static DivisionTableRow Row(int rank) =>
        new(rank, Guid.CreateVersion7(), $"Club {rank}", $"C{rank:00}", 30, 10, 10, 10, 40, 40, 40, 0, 0);

    private static FixtureRow Fixture(PlayoffStage? stage) =>
        new(
            Guid.CreateVersion7(),
            HomeClubId,
            AwayClubId,
            DateTimeOffset.UnixEpoch,
            FixtureStatus.Scheduled,
            null,
            null,
            null,
            false,
            stage);

    private static PlayoffMatchRow Shootout() =>
        new(
            PlayoffStage.Final,
            PlayoffDecision.Penalties,
            AwayClubId,
            3,
            4,
            "home",
            [
                new PlayoffKickRow(1, 1, "home", "Home Taker", "Away Keeper", "scored"),
                new PlayoffKickRow(2, 1, "away", "Away Taker", "Home Keeper", "saved"),
            ]);
}

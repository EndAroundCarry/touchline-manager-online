using FluentAssertions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Tests.Match;

/// <summary>
/// The season-statistics rule: a stored result and its events become one line per player who appeared
/// (`STA-*`).
/// </summary>
public sealed class SeasonStatisticsCalculatorTests
{
    private static readonly Guid FixtureId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClubId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Striker = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Keeper = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Bench = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid OpponentKeeper = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void A_player_who_did_not_appear_produces_no_line()
    {
        var lines = SeasonStatisticsCalculator.Calculate([Row()], []);

        lines.Should().NotContain(line => line.PlayerId == Bench);
        lines.Should().HaveCount(2);
    }

    [Fact]
    public void The_line_carries_the_goals_assists_and_rating_the_result_recorded()
    {
        var lines = SeasonStatisticsCalculator.Calculate([Row()], []);

        var striker = lines.Single(line => line.PlayerId == Striker);

        striker.ClubId.Should().Be(ClubId);
        striker.Appearances.Should().Be(1);
        striker.Starts.Should().Be(1);
        striker.MinutesPlayed.Should().Be(90);
        striker.Goals.Should().Be(1);
        striker.Assists.Should().Be(1);
        striker.RatingBasisPoints.Should().Be(8_000);
    }

    [Fact]
    public void Shots_and_saves_are_counted_from_the_event_stream()
    {
        MatchStatEvent[] events =
        [
            new(FixtureId, MatchEventType.Goal, Striker, OpponentKeeper),
            new(FixtureId, MatchEventType.ShotSaved, Striker, OpponentKeeper),
            new(FixtureId, MatchEventType.Corner, null, null),
        ];

        var lines = SeasonStatisticsCalculator.Calculate([Row()], events);

        var striker = lines.Single(line => line.PlayerId == Striker);
        striker.Shots.Should().Be(2, "a goal and a saved shot are both shots");
        striker.ShotsOnTarget.Should().Be(2);

        // The opponent's keeper is not in this fixture's result, so no line is produced for it, but the
        // save is not attributed to our striker either.
        striker.Saves.Should().Be(0);
    }

    [Fact]
    public void A_goalkeepers_saves_are_counted_from_the_shot_saved_events()
    {
        MatchStatEvent[] events =
        [
            new(FixtureId, MatchEventType.ShotSaved, Striker, Keeper),
            new(FixtureId, MatchEventType.PenaltyMissed, Striker, Keeper),
        ];

        var lines = SeasonStatisticsCalculator.Calculate([Row()], events);

        lines.Single(line => line.PlayerId == Keeper).Saves.Should().Be(1);
        lines.Single(line => line.PlayerId == Striker).Shots.Should().Be(2);
    }

    [Fact]
    public void The_lines_are_ordered_by_club_and_player()
    {
        var lines = SeasonStatisticsCalculator.Calculate([Row()], []);

        lines.Select(line => line.PlayerId)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void Aggregate_sums_a_players_matches_into_one_season_line()
    {
        var lines = SeasonStatisticsCalculator.Aggregate(
        [
            MatchLine(Striker, ClubId, minutes: 90, goals: 1, rating: 7_000),
            MatchLine(Striker, ClubId, minutes: 60, goals: 2, rating: 8_000),
        ]);

        lines.Should().HaveCount(1);

        var season = lines[0];

        season.PlayerId.Should().Be(Striker);
        season.Appearances.Should().Be(2);
        season.MinutesPlayed.Should().Be(150);
        season.Goals.Should().Be(3);
        season.RatingBasisPointsTotal.Should().Be(15_000);
        season.RatedAppearances.Should().Be(2);
    }

    [Fact]
    public void Aggregate_keeps_a_transferred_players_two_clubs_apart()
    {
        var otherClub = Guid.CreateVersion7();

        var lines = SeasonStatisticsCalculator.Aggregate(
        [
            MatchLine(Striker, ClubId, minutes: 90, goals: 1, rating: 7_000),
            MatchLine(Striker, otherClub, minutes: 90, goals: 2, rating: 8_000),
        ]);

        lines.Should().HaveCount(2, "a player who moved clubs has a line for each");
        lines.Should().OnlyContain(line => line.PlayerId == Striker);
    }

    [Fact]
    public void Aggregate_counts_only_rated_appearances()
    {
        var lines = SeasonStatisticsCalculator.Aggregate(
        [
            MatchLine(Striker, ClubId, minutes: 90, goals: 1, rating: 7_000),
            MatchLine(Striker, ClubId, minutes: 90, goals: 0, rating: 0),
        ]);

        lines[0].Appearances.Should().Be(2);
        lines[0].RatedAppearances.Should().Be(1);
        lines[0].RatingBasisPointsTotal.Should().Be(7_000);
    }

    /// <summary>One player's contribution to one match, as the calculator would produce it.</summary>
    private static PlayerMatchStatLine MatchLine(Guid playerId, Guid clubId, int minutes, int goals, int rating) => new()
    {
        PlayerId = playerId,
        ClubId = clubId,
        Appearances = 1,
        Starts = 1,
        MinutesPlayed = minutes,
        Goals = goals,
        Assists = 0,
        Shots = 0,
        ShotsOnTarget = 0,
        Saves = 0,
        YellowCards = 0,
        RedCards = 0,
        RatingBasisPoints = rating,
    };

    /// <summary>A stored result with a striker and a keeper on the pitch and one unused substitute.</summary>
    private static FixtureMatchLoadRow Row()
    {
        var result = new MatchResultV1
        {
            EngineVersion = EngineVersions.EngineLabel,
            RuleSetVersion = EngineVersions.RuleSetLabel,
            HomeGoals = 1,
            AwayGoals = 0,
            Home = Statistics(),
            Away = Statistics(),
            Events = [],
            PlayerLines =
            [
                Line(Striker, started: true, minutes: 90, goals: 1, assists: 1, rating: 8_000),
                Line(Keeper, started: true, minutes: 90, goals: 0, assists: 0, rating: 7_500),
                Line(Bench, started: false, minutes: 0, goals: 0, assists: 0, rating: 0),
            ],
            TotalMinutesPlayed = 94,
            InputHash = "input",
            OutputHash = "output",
        };

        return new FixtureMatchLoadRow(
            FixtureId,
            result.HomeGoals,
            result.AwayGoals,
            "snapshot",
            MatchStatisticsDocument.Write(result));
    }

    private static MatchPlayerLineV1 Line(
        Guid playerId,
        bool started,
        int minutes,
        int goals,
        int assists,
        int rating) => new()
        {
            ParticipantId = playerId,
            ClubId = ClubId,
            Side = MatchSide.Home,
            Started = started,
            MinutesPlayed = minutes,
            Goals = goals,
            Assists = assists,
            YellowCards = 0,
            SentOff = false,
            AbsenceFixtures = 0,
            RatingBasisPoints = rating,
            FinalConditionBasisPoints = 6_000,
        };

    private static MatchStatisticsV1 Statistics() => new()
    {
        PossessionBasisPoints = 5_000,
        Goals = 0,
        Shots = 0,
        ShotsOnTarget = 0,
        ShotsOffTarget = 0,
        ShotsBlocked = 0,
        WoodworkHits = 0,
        Saves = 0,
        Corners = 0,
        Offsides = 0,
        Fouls = 0,
        YellowCards = 0,
        RedCards = 0,
        PenaltiesAwarded = 0,
        PenaltiesScored = 0,
        Injuries = 0,
        Substitutions = 0,
    };
}

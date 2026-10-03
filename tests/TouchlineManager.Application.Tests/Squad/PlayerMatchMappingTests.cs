using FluentAssertions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Match;
using TouchlineManager.Application.Squad;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Tests.Squad;

/// <summary>
/// A player's line of one stored match: the result document says who played and what they did, and the
/// counted shots and saves ride beside it (`STA-2`).
/// </summary>
public sealed class PlayerMatchMappingTests
{
    private static readonly Guid HomeClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AwayClub = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Striker = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Winger = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Bench = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public void A_home_players_line_reads_the_match_from_their_side()
    {
        var response = Source().ToMatchResponse(Striker);

        response.Should().NotBeNull();
        response!.Home.Should().BeTrue();
        response.OpponentClubId.Should().Be(AwayClub);
        response.OpponentName.Should().Be("Vale Rovers");
        response.GoalsFor.Should().Be(2);
        response.GoalsAgainst.Should().Be(1);
        response.Round.Should().Be(7);
        response.Started.Should().BeTrue();
        response.MinutesPlayed.Should().Be(90);
        response.Goals.Should().Be(1);
        response.Assists.Should().Be(1);
        response.Shots.Should().Be(3);
        response.ShotsOnTarget.Should().Be(2);
        response.PassesAttempted.Should().Be(50);
        response.PassesCompleted.Should().Be(41);
        response.DribblesAttempted.Should().Be(6);
        response.DribblesCompleted.Should().Be(3);
        response.RedCards.Should().Be(0);
        response.Rating.Should().Be(8.0m, "TRN-8: the rating crosses the wire on its display scale");
    }

    [Fact]
    public void An_away_players_line_swaps_the_score_and_the_opponent()
    {
        var response = Source().ToMatchResponse(Winger);

        response!.Home.Should().BeFalse();
        response.OpponentClubId.Should().Be(HomeClub);
        response.OpponentName.Should().Be("Ashvale United");
        response.GoalsFor.Should().Be(1);
        response.GoalsAgainst.Should().Be(2);
        response.YellowCards.Should().Be(1);
        response.RedCards.Should().Be(1);
    }

    [Fact]
    public void A_player_who_did_not_take_the_pitch_has_no_row()
    {
        Source().ToMatchResponse(Bench).Should().BeNull();
    }

    private static SquadPlayerMatchSource Source()
    {
        var result = new MatchResultV1
        {
            EngineVersion = EngineVersions.EngineLabel,
            RuleSetVersion = EngineVersions.RuleSetLabel,
            HomeGoals = 2,
            AwayGoals = 1,
            Home = Statistics(),
            Away = Statistics(),
            Events = [],
            PlayerLines =
            [
                Line(Striker, HomeClub, MatchSide.Home, started: true, minutes: 90, goals: 1, assists: 1, yellow: 0, sentOff: false, rating: 8_000),
                Line(Winger, AwayClub, MatchSide.Away, started: true, minutes: 60, goals: 0, assists: 0, yellow: 1, sentOff: true, rating: 4_000),
                Line(Bench, HomeClub, MatchSide.Home, started: false, minutes: 0, goals: 0, assists: 0, yellow: 0, sentOff: false, rating: 0),
            ],
            TotalMinutesPlayed = 94,
            InputHash = "input",
            OutputHash = "output",
        };

        return new SquadPlayerMatchSource(
            Guid.CreateVersion7(),
            SeasonNumber: 2,
            SeasonLabel: "Season 2",
            RoundNumber: 7,
            KickoffAt: new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero),
            HomeClubId: HomeClub,
            HomeClubName: "Ashvale United",
            AwayClubId: AwayClub,
            AwayClubName: "Vale Rovers",
            HomeGoals: 2,
            AwayGoals: 1,
            StatisticsJson: MatchStatisticsDocument.Write(result),
            Shots: 3,
            ShotsOnTarget: 2,
            Saves: 0);
    }

    private static MatchPlayerLineV1 Line(
        Guid playerId,
        Guid clubId,
        MatchSide side,
        bool started,
        int minutes,
        int goals,
        int assists,
        int yellow,
        bool sentOff,
        int rating) => new()
        {
            ParticipantId = playerId,
            ClubId = clubId,
            Side = side,
            Started = started,
            MinutesPlayed = minutes,
            Goals = goals,
            Assists = assists,
            YellowCards = yellow,
            SentOff = sentOff,
            AbsenceFixtures = 0,
            RatingBasisPoints = rating,
            FinalConditionBasisPoints = 6_000,
            PassesAttempted = minutes == 0 ? 0 : 50,
            PassesCompleted = minutes == 0 ? 0 : 41,
            DribblesAttempted = minutes == 0 ? 0 : 6,
            DribblesCompleted = minutes == 0 ? 0 : 3,
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

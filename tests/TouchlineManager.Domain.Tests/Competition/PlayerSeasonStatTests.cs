using FluentAssertions;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Domain.Tests.Competition;

/// <summary>
/// A player's season statistics: the accumulation, its refusals, and the average it computes
/// (`STA-*`, master plan §6.4).
/// </summary>
public sealed class PlayerSeasonStatTests
{
    private static readonly Guid DivisionSeasonId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClubId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Opening_requires_an_owner_identity()
    {
        var act = () => PlayerSeasonStat.Open(Guid.CreateVersion7(), Guid.Empty, PlayerId, ClubId, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_new_line_has_no_average_rating()
    {
        var stat = Open();

        stat.Appearances.Should().Be(0);
        stat.AverageRatingBasisPoints.Should().BeNull("a player with no appearances has no average");
    }

    [Fact]
    public void A_line_for_another_player_is_refused()
    {
        var stat = Open();
        var line = Line() with { PlayerId = Guid.CreateVersion7() };

        var act = () => stat.Accumulate(line, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_line_for_another_club_is_refused()
    {
        var stat = Open();
        var line = Line() with { ClubId = Guid.CreateVersion7() };

        var act = () => stat.Accumulate(line, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_negative_count_is_refused()
    {
        var stat = Open();
        var line = Line() with { Goals = -1 };

        var act = () => stat.Accumulate(line, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_line_without_an_appearance_is_refused()
    {
        var stat = Open();
        var line = Line() with { Appearances = 0, Starts = 0, MinutesPlayed = 0, Goals = 0 };

        var act = () => stat.Accumulate(line, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void More_shots_on_target_than_shots_is_refused()
    {
        var stat = Open();
        var line = Line() with { Shots = 2, ShotsOnTarget = 3 };

        var act = () => stat.Accumulate(line, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void More_completed_passes_than_attempted_is_refused()
    {
        var act = () => Open().Accumulate(Line() with { PassesAttempted = 10, PassesCompleted = 11 }, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void More_successful_dribbles_than_attempted_is_refused()
    {
        var act = () => Open().Accumulate(Line() with { DribblesAttempted = 2, DribblesCompleted = 3 }, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_negative_pass_count_is_refused()
    {
        var act = () => Open().Accumulate(Line() with { PassesAttempted = -1, PassesCompleted = -1 }, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_season_line_with_more_completed_passes_than_attempted_is_refused()
    {
        var act = () => Open().Rebuild(SeasonLine() with { PassesAttempted = 5, PassesCompleted = 6 }, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Accumulating_sums_the_passes_and_take_ons()
    {
        var stat = Open();

        stat.Accumulate(Line() with { PassesAttempted = 40, PassesCompleted = 30, DribblesAttempted = 6, DribblesCompleted = 3 }, Now);
        stat.Accumulate(Line() with { PassesAttempted = 20, PassesCompleted = 19, DribblesAttempted = 2, DribblesCompleted = 2 }, Now);

        stat.PassesAttempted.Should().Be(60);
        stat.PassesCompleted.Should().Be(49);
        stat.DribblesAttempted.Should().Be(8);
        stat.DribblesCompleted.Should().Be(5);
    }

    [Fact]
    public void Accumulating_sums_the_totals_and_averages_the_ratings()
    {
        var stat = Open();

        stat.Accumulate(Line() with { Appearances = 1, Starts = 1, MinutesPlayed = 90, Goals = 2, Assists = 1, RatingBasisPoints = 8_000 }, Now);
        stat.Accumulate(Line() with { Appearances = 1, Starts = 0, MinutesPlayed = 30, Goals = 0, Assists = 1, RatingBasisPoints = 6_000 }, Now);

        stat.Appearances.Should().Be(2);
        stat.Starts.Should().Be(1);
        stat.MinutesPlayed.Should().Be(120);
        stat.Goals.Should().Be(2);
        stat.Assists.Should().Be(2);
        stat.AverageRatingBasisPoints.Should().Be(7_000);
        stat.Version.Should().Be(3, "opening is the first version and each match advances it");
    }

    [Fact]
    public void A_match_that_carried_no_rating_does_not_lower_the_average()
    {
        var stat = Open();

        stat.Accumulate(Line() with { Appearances = 1, Starts = 1, MinutesPlayed = 90, RatingBasisPoints = 8_000 }, Now);

        // The second match has no rating (for example a reader of an older document's shape): it must be an
        // appearance, but it must not count as a zero in the average.
        stat.Accumulate(Line() with { Appearances = 1, Starts = 1, MinutesPlayed = 90, RatingBasisPoints = 0 }, Now);

        stat.Appearances.Should().Be(2);
        stat.RatedAppearances.Should().Be(1);
        stat.AverageRatingBasisPoints.Should().Be(8_000);
    }

    [Fact]
    public void Create_opens_a_line_already_holding_the_seasons_totals()
    {
        var stat = PlayerSeasonStat.Create(Guid.CreateVersion7(), DivisionSeasonId, SeasonLine(), Now);

        stat.Appearances.Should().Be(10);
        stat.Starts.Should().Be(9);
        stat.Goals.Should().Be(4);
        stat.Assists.Should().Be(3);
        stat.AverageRatingBasisPoints.Should().Be(7_000);
        stat.Version.Should().Be(1, "a created line is at its first version");
    }

    [Fact]
    public void Rebuild_replaces_the_totals_rather_than_adding_to_them()
    {
        var stat = Open();

        stat.Accumulate(
            Line() with { Appearances = 1, Starts = 1, MinutesPlayed = 90, Goals = 5, RatingBasisPoints = 9_000 },
            Now);

        stat.Rebuild(SeasonLine(), Now);

        stat.Appearances.Should().Be(10);
        stat.Goals.Should().Be(4, "a rebuild sets the totals instead of summing them");
        stat.Assists.Should().Be(3);
        stat.MinutesPlayed.Should().Be(900);
        stat.RatingBasisPointsTotal.Should().Be(70_000);
        stat.RatedAppearances.Should().Be(10);
        stat.AverageRatingBasisPoints.Should().Be(7_000);
    }

    [Fact]
    public void Rebuild_refuses_another_players_line()
    {
        var stat = Open();

        var act = () => stat.Rebuild(SeasonLine() with { PlayerId = Guid.CreateVersion7() }, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_rebuilt_line_with_no_appearances_is_refused()
    {
        var stat = Open();

        var act = () => stat.Rebuild(SeasonLine() with { Appearances = 0, Starts = 0 }, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_rebuilt_line_with_more_starts_than_appearances_is_refused()
    {
        var stat = Open();

        var act = () => stat.Rebuild(SeasonLine() with { Appearances = 3, Starts = 4 }, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_rebuilt_line_whose_rating_total_exceeds_its_rated_appearances_is_refused()
    {
        var stat = Open();

        var act = () => stat.Rebuild(SeasonLine() with { RatedAppearances = 2, RatingBasisPointsTotal = 25_000 }, Now);

        act.Should().Throw<ArgumentException>();
    }

    private static PlayerSeasonStat Open() =>
        PlayerSeasonStat.Open(Guid.CreateVersion7(), DivisionSeasonId, PlayerId, ClubId, Now);

    private static PlayerMatchStatLine Line() => new()
    {
        PlayerId = PlayerId,
        ClubId = ClubId,
        Appearances = 1,
        Starts = 1,
        MinutesPlayed = 90,
        Goals = 1,
        Assists = 0,
        Shots = 2,
        ShotsOnTarget = 1,
        Saves = 0,
        PassesAttempted = 40,
        PassesCompleted = 32,
        DribblesAttempted = 5,
        DribblesCompleted = 2,
        YellowCards = 0,
        RedCards = 0,
        RatingBasisPoints = 7_000,
    };

    private static PlayerSeasonStatLine SeasonLine() => new()
    {
        PlayerId = PlayerId,
        ClubId = ClubId,
        Appearances = 10,
        Starts = 9,
        MinutesPlayed = 900,
        Goals = 4,
        Assists = 3,
        Shots = 20,
        ShotsOnTarget = 10,
        Saves = 0,
        PassesAttempted = 400,
        PassesCompleted = 320,
        DribblesAttempted = 50,
        DribblesCompleted = 20,
        YellowCards = 2,
        RedCards = 0,
        RatingBasisPointsTotal = 70_000,
        RatedAppearances = 10,
    };
}

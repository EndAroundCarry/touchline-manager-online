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
        YellowCards = 0,
        RedCards = 0,
        RatingBasisPoints = 7_000,
    };
}

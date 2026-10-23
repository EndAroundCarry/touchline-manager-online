using FluentAssertions;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Domain.Tests.Competition;

/// <summary>The table zones the table page names in words (`PR-1`).</summary>
public sealed class LeagueZonesTests
{
    [Theory]
    [InlineData(1, LeagueZone.Promotion)]
    [InlineData(2, LeagueZone.Promotion)]
    [InlineData(3, LeagueZone.Playoff)]
    [InlineData(4, LeagueZone.Playoff)]
    [InlineData(5, LeagueZone.None)]
    [InlineData(16, LeagueZone.None)]
    public void A_league_that_feeds_another_marks_its_top_places(int place, LeagueZone expected)
    {
        LeagueZones.For(hasParent: true, hasFeeder: false, place).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, LeagueZone.None)]
    [InlineData(4, LeagueZone.None)]
    [InlineData(13, LeagueZone.None)]
    [InlineData(14, LeagueZone.Playoff)]
    [InlineData(15, LeagueZone.Relegation)]
    [InlineData(16, LeagueZone.Relegation)]
    public void A_league_fed_by_another_marks_its_bottom_places(int place, LeagueZone expected)
    {
        LeagueZones.For(hasParent: false, hasFeeder: true, place).Should().Be(expected);
    }

    [Fact]
    public void A_middle_league_marks_both_ends()
    {
        var zones = Enumerable.Range(1, 16).Select(place => LeagueZones.For(true, true, place)).ToList();

        zones.Take(2).Should().OnlyContain(zone => zone == LeagueZone.Promotion);
        zones.Skip(2).Take(2).Should().OnlyContain(zone => zone == LeagueZone.Playoff);
        zones.Skip(4).Take(9).Should().OnlyContain(zone => zone == LeagueZone.None);
        zones[13].Should().Be(LeagueZone.Playoff);
        zones.Skip(14).Should().OnlyContain(zone => zone == LeagueZone.Relegation);
    }

    [Fact]
    public void A_one_league_country_marks_nothing()
    {
        Enumerable.Range(1, 16)
            .Select(place => LeagueZones.For(false, false, place))
            .Should().OnlyContain(zone => zone == LeagueZone.None);
    }

    [Fact]
    public void A_place_outside_the_table_is_a_programming_error()
    {
        var act = () => LeagueZones.For(true, true, 17);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Zones_have_codes_and_words()
    {
        LeagueZone.Playoff.ToCode().Should().Be("playoff");
        LeagueZone.Promotion.Label().Should().Be("Promotion");
        LeagueZone.Playoff.Label().Should().Be("Play-off");
        LeagueZone.Relegation.Label().Should().Be("Relegation");
        LeagueZone.None.Label().Should().BeEmpty();
    }
}

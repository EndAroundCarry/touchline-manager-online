using FluentAssertions;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.World;

/// <summary>
/// The stadium screen quotes the crowd from the club's own fans and their mood (`STAD-5`).
/// </summary>
public sealed class StadiumMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static StadiumResponse Quote(int tier, int fans, int moodBp) =>
        ClubStadium.Open(Guid.NewGuid(), Guid.NewGuid(), Now)
            .ToResponse(new ClubStadiumContext(tier, 1_000_000, 0, fans, moodBp), Now);

    [Fact]
    public void A_new_tier_one_club_is_quoted_three_thousand_people_and_the_gate_of_the_places_they_fill()
    {
        var quote = Quote(1, FanRuleSet.StartFansFor(1), FanRuleSet.CalmMoodBp);

        quote.ExpectedDemand.Should().Be(3_000);
        quote.Stands.Sum(stand => stand.ExpectedSold).Should().Be(2_890);
        quote.ExpectedGateMinor.Should().BeLessThan(quote.FullHouseMinor);
    }

    [Fact]
    public void The_quote_moves_with_the_fans_and_with_their_mood()
    {
        var opening = Quote(1, 10_000, FanRuleSet.CalmMoodBp);

        Quote(1, 20_000, FanRuleSet.CalmMoodBp).ExpectedDemand.Should().Be(6_000);
        Quote(1, 10_000, FanRuleSet.MoodMaxBp).ExpectedDemand.Should().BeGreaterThan(opening.ExpectedDemand);
        Quote(1, 10_000, 0).ExpectedDemand.Should().BeLessThan(opening.ExpectedDemand);
        Quote(1, 30_000, FanRuleSet.CalmMoodBp).ExpectedGateMinor
            .Should().BeGreaterThan(opening.ExpectedGateMinor, "a bigger crowd fills more of the opening ground");
    }
}

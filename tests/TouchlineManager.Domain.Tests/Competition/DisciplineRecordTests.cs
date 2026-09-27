using FluentAssertions;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Competition;

/// <summary>
/// A player's card accumulation and the suspension it earns (`DIS-2`, `DIS-4`), and the injury bands the
/// match effects read (`DIS-1`).
/// </summary>
public sealed class DisciplineRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    private static DisciplineRecord Record() =>
        DisciplineRecord.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    [Fact]
    public void A_new_record_opens_with_no_cards()
    {
        var record = Record();

        record.YellowCards.Should().Be(0);
        record.RedCards.Should().Be(0);
        record.Version.Should().Be(1);
        record.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Cards_accumulate_and_advance_the_version()
    {
        var record = Record();

        record.AddCards(yellowCards: 2, redCards: 0, Now.AddDays(1));

        record.YellowCards.Should().Be(2, "DIS-2: yellows accumulate");
        record.RedCards.Should().Be(0);
        record.Version.Should().Be(2);

        record.AddCards(yellowCards: 1, redCards: 1, Now.AddDays(2));

        record.YellowCards.Should().Be(3);
        record.RedCards.Should().Be(1, "DIS-4: a sending-off is recorded");
        record.UpdatedAt.Should().Be(Now.AddDays(2));
    }

    [Fact]
    public void Every_multiple_of_the_threshold_earns_a_suspension_without_a_reset()
    {
        var record = Record();
        var threshold = WorldRuleSet.YellowSuspensionThreshold;

        record.YellowSuspensionsEarned(4, threshold).Should().Be(0, "the fourth booking is not a fifth");
        record.AddCards(4, 0, Now);

        record.YellowSuspensionsEarned(1, threshold).Should().Be(1, "the fifth booking is a ban (DIS-2)");
        record.AddCards(1, 0, Now);

        // DIS-3 clears the accumulation at rollover and nowhere else, so the tenth booking is a ban too.
        record.YellowSuspensionsEarned(4, threshold).Should().Be(0);
        record.YellowSuspensionsEarned(5, threshold).Should().Be(1, "the tenth booking crosses the next multiple");
    }

    [Fact]
    public void A_record_needs_a_division_season_a_player_and_at_least_one_card()
    {
        var openWithoutSeason = () => DisciplineRecord.Open(Guid.CreateVersion7(), Guid.Empty, Guid.CreateVersion7(), Now);
        var openWithoutPlayer = () => DisciplineRecord.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty, Now);
        var addNothing = () => Record().AddCards(0, 0, Now);
        var addNegative = () => Record().AddCards(-1, 0, Now);

        openWithoutSeason.Should().Throw<ArgumentException>();
        openWithoutPlayer.Should().Throw<ArgumentException>();
        addNothing.Should().Throw<ArgumentException>();
        addNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1, InjurySeverity.Minor)]
    [InlineData(2, InjurySeverity.Minor)]
    [InlineData(3, InjurySeverity.Moderate)]
    [InlineData(4, InjurySeverity.Moderate)]
    [InlineData(5, InjurySeverity.Major)]
    [InlineData(6, InjurySeverity.Major)]
    public void An_injury_absence_maps_to_its_band(int absenceFixtures, InjurySeverity expected)
    {
        WorldRuleSet.InjurySeverityFor(absenceFixtures).Should().Be(expected, $"DIS-1 band for {absenceFixtures}");
    }

    [Fact]
    public void An_absence_outside_the_rule_sets_range_is_refused()
    {
        var none = () => WorldRuleSet.InjurySeverityFor(0);
        var tooLong = () => WorldRuleSet.InjurySeverityFor(7);

        none.Should().Throw<ArgumentOutOfRangeException>("DIS-1: an injury is at least one fixture");
        tooLong.Should().Throw<ArgumentOutOfRangeException>("DIS-1: an injury is at most six fixtures");
    }
}

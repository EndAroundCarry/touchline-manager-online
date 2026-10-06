using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// A club's ground: what it opens with, how a level is read from its size, and how it grows (`STAD-1`,
/// `STAD-2`, `STAD-6`).
/// </summary>
public sealed class ClubStadiumTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static ClubStadium Opened() => ClubStadium.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    [Fact]
    public void Every_ground_opens_with_five_thousand_places_split_across_the_four_stands()
    {
        var stadium = Opened();

        stadium.Seats.Should().Be(new StadiumSeats(Standing: 3_000, Seating: 1_000, CoveredSeating: 900, Vip: 100));
        stadium.Capacity.Should().Be(5_000);
        stadium.Level.Should().Be(1);
        stadium.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5_000, 1)]
    [InlineData(5_001, 2)]
    [InlineData(10_000, 2)]
    [InlineData(10_001, 3)]
    [InlineData(45_000, 9)]
    [InlineData(45_001, 10)]
    [InlineData(50_000, 10)]
    public void The_level_is_the_number_of_five_thousand_place_blocks_the_ground_spans(int capacity, int level) =>
        StadiumRuleSet.LevelFor(capacity).Should().Be(level);

    [Theory]
    [InlineData(0)]
    [InlineData(50_001)]
    public void A_capacity_outside_the_ten_levels_is_a_programming_error(int capacity)
    {
        var act = () => StadiumRuleSet.LevelFor(capacity);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void One_place_past_the_end_of_a_block_moves_the_ground_up_a_level()
    {
        var stadium = Opened();

        stadium.AddSeats(StadiumStand.Standing, 1, Now.AddDays(1));

        stadium.Capacity.Should().Be(5_001);
        stadium.Level.Should().Be(2, "the picture changes when the ground passes 5,000");
    }

    [Fact]
    public void Adding_places_grows_only_that_stand_and_moves_the_version_on()
    {
        var stadium = Opened();
        var later = Now.AddHours(3);

        stadium.AddSeats(StadiumStand.CoveredSeating, 250, later);

        stadium.Seats.Should().Be(new StadiumSeats(3_000, 1_000, 1_150, 100));
        stadium.Version.Should().Be(2);
        stadium.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void A_manager_can_add_places_of_every_kind()
    {
        var stadium = Opened();

        foreach (var stand in StadiumStands.All)
        {
            stadium.AddSeats(stand, 10, Now);
        }

        stadium.Seats.Should().Be(new StadiumSeats(3_010, 1_010, 910, 110));
    }

    [Fact]
    public void The_ground_takes_places_right_up_to_the_largest_size_and_no_further()
    {
        var stadium = Opened();

        stadium.AddSeats(StadiumStand.Standing, StadiumRuleSet.MaxCapacity - 5_000, Now);

        stadium.Capacity.Should().Be(50_000);
        stadium.Level.Should().Be(10);
        stadium.RemainingRoom.Should().Be(0);

        var act = () => stadium.AddSeats(StadiumStand.Vip, 1, Now);

        act.Should().Throw<InvalidOperationException>("the top level is the largest ground (STAD-1)");
        stadium.Capacity.Should().Be(50_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void An_order_must_add_at_least_one_place(int count)
    {
        var act = () => Opened().AddSeats(StadiumStand.Standing, count, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("standing", StadiumStand.Standing)]
    [InlineData("seating", StadiumStand.Seating)]
    [InlineData("covered_seating", StadiumStand.CoveredSeating)]
    [InlineData("vip", StadiumStand.Vip)]
    public void A_stand_round_trips_through_its_stable_code(string code, StadiumStand stand)
    {
        stand.ToCode().Should().Be(code);
        StadiumStands.FromCode(code).Should().Be(stand);
        StadiumStands.TryFromCode(code, out var parsed).Should().BeTrue();
        parsed.Should().Be(stand);
    }

    [Theory]
    [InlineData("")]
    [InlineData("terrace")]
    [InlineData("Standing")]
    [InlineData(null)]
    public void An_unknown_stand_code_is_not_parsed(string? code) =>
        StadiumStands.TryFromCode(code, out _).Should().BeFalse();
}

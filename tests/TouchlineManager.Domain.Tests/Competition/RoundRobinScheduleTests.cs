using FluentAssertions;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Tests.Competition;

/// <summary>
/// The fixture list generator and its properties (`CAL-8`, `CAL-9`).
/// </summary>
/// <remarks>
/// A schedule is generated, not searched: the circle method produces a single round-robin and the second
/// half mirrors it, so the properties below hold by construction. These tests are what turn that argument
/// into evidence, across every plausible division size and many seeds, because a schedule that quietly
/// failed one property would be a season that could not be played correctly.
/// </remarks>
public sealed class RoundRobinScheduleTests
{
    /// <summary>The even division sizes a pyramid could plausibly hold.</summary>
    public static TheoryData<int> DivisionSizes => [2, 4, 6, 8, 10, 12, 14, 16, 18, 20];

    [Theory]
    [MemberData(nameof(DivisionSizes))]
    public void A_generated_schedule_satisfies_every_schedule_rule_for_many_seeds(int size)
    {
        var clubs = Clubs(size);

        for (ulong seed = 0; seed < 50; seed++)
        {
            var schedule = RoundRobinSchedule.Generate(clubs, seed);

            var issues = ScheduleValidator.Validate(clubs, schedule);

            issues.Should().BeEmpty(
                "the schedule for {0} clubs and seed {1} must satisfy CAL-9, but found: {2}",
                size,
                seed,
                string.Join("; ", issues.Select(issue => issue.Detail)));
        }
    }

    [Fact]
    public void A_division_schedule_is_thirty_four_rounds_of_nine_matches()
    {
        var clubs = Clubs(WorldRuleSet.ClubsPerDivision);

        var schedule = RoundRobinSchedule.Generate(clubs, seed: 7);

        schedule.Should().HaveCount(WorldRuleSet.MatchdaysPerSeason, "CAL-1");
        schedule.Should().OnlyContain(round => round.Pairings.Count == WorldRuleSet.ClubsPerDivision / 2);

        for (var round = 0; round < schedule.Count; round++)
        {
            schedule[round].RoundNumber.Should().Be(round + 1);
        }
    }

    [Fact]
    public void The_same_seed_and_clubs_reproduce_the_same_fixture_list()
    {
        var clubs = Clubs(18);

        var first = RoundRobinSchedule.Generate(clubs, seed: 42);
        var second = RoundRobinSchedule.Generate(clubs, seed: 42);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_different_seed_produces_a_different_fixture_list()
    {
        var clubs = Clubs(18);

        var first = RoundRobinSchedule.Generate(clubs, seed: 1);
        var second = RoundRobinSchedule.Generate(clubs, seed: 2);

        second.Should().NotBeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Every_club_plays_every_other_once_home_and_once_away()
    {
        var clubs = Clubs(18);

        var schedule = RoundRobinSchedule.Generate(clubs, seed: 11);

        foreach (var club in clubs)
        {
            var home = schedule
                .SelectMany(round => round.Pairings)
                .Count(pairing => pairing.HomeClubId == club);

            var away = schedule
                .SelectMany(round => round.Pairings)
                .Count(pairing => pairing.AwayClubId == club);

            home.Should().Be(17, "a club hosts each of the other seventeen once");
            away.Should().Be(17, "and visits each of them once");
        }
    }

    [Fact]
    public void The_generated_order_does_not_depend_on_the_input_order()
    {
        // Reproducibility means the same logical division, and the caller supplies clubs in name order.
        // Permuting the input order must therefore not silently produce the same fixture list by accident:
        // the shuffle is seeded, so a different order is a different draw.
        var clubs = Clubs(18);
        var reversed = clubs.AsEnumerable().Reverse().ToList();

        var schedule = RoundRobinSchedule.Generate(clubs, seed: 5);
        var other = RoundRobinSchedule.Generate(reversed, seed: 5);

        ScheduleValidator.Validate(reversed, other).Should().BeEmpty();
        other.Should().NotBeEquivalentTo(schedule, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(19)]
    public void An_odd_number_of_clubs_is_refused(int size)
    {
        var act = () => RoundRobinSchedule.Generate(Clubs(size), seed: 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Fewer_than_two_clubs_or_a_repeated_club_is_refused()
    {
        var single = () => RoundRobinSchedule.Generate(Clubs(1), seed: 0);
        var repeated = () => RoundRobinSchedule.Generate([Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()], seed: 0);
        var duplicated = Guid.CreateVersion7();
        var duplicate = () => RoundRobinSchedule.Generate([duplicated, duplicated, Guid.CreateVersion7(), Guid.CreateVersion7()], seed: 0);

        single.Should().Throw<ArgumentException>();
        repeated.Should().NotThrow();
        duplicate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_validator_reports_a_malformed_schedule_rather_than_accepting_it()
    {
        // The validator exists to catch a generation bug, so it must reject a schedule that breaks a rule.
        var clubs = Clubs(4);

        // Four clubs should play six rounds; drop the last.
        var truncated = RoundRobinSchedule.Generate(clubs, seed: 0).SkipLast(1).ToList();

        ScheduleValidator.Validate(clubs, truncated)
            .Should().Contain(issue => issue.Code == ScheduleIssueCodes.RoundCountIncorrect);
    }

    private static List<Guid> Clubs(int size) =>
        [.. Enumerable.Range(0, size).Select(_ => Guid.CreateVersion7())];
}

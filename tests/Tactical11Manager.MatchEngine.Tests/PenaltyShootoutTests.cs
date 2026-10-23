using FluentAssertions;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Ratings;
using Tactical11Manager.MatchEngine.Shootout;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>
/// The penalty shoot-out that decides a drawn play-off, outside the match engine.
/// </summary>
/// <remarks>
/// The shoot-out is a separate pure function, so these tests pin it on its own: the same seed gives the same kicks,
/// the stop rule and the sudden death follow the usual rules, and the takers come in the documented order. The
/// existing golden-hash and determinism suites, which this change does not touch, are the proof the match engine
/// itself is unchanged.
/// </remarks>
public sealed class PenaltyShootoutTests
{
    private const int Regulation = 5;

    private static EngineRulesV2 Rules => TestMatchFactory.Rules;

    [Fact]
    public void The_same_seed_gives_the_same_shootout()
    {
        var (home, away) = EvenSides();

        var first = PenaltyShootout.Run(7, home, away, Regulation, Rules);
        var second = PenaltyShootout.Run(7, home, away, Regulation, Rules);

        second.Should().BeEquivalentTo(first);
        first.Version.Should().Be("shootout-v1");
    }

    [Fact]
    public void Different_seeds_give_different_shootouts()
    {
        var (home, away) = EvenSides();

        var results = Enumerable.Range(1, 40)
            .Select(seed => string.Join(string.Empty, PenaltyShootout.Run((ulong)seed, home, away, Regulation, Rules).Kicks.Select(kick => (int)kick.Outcome)))
            .Distinct()
            .Count();

        results.Should().BeGreaterThan(5);
    }

    [Fact]
    public void A_pinned_seed_gives_a_pinned_kick_sequence()
    {
        var (home, away) = EvenSides();

        var result = PenaltyShootout.Run(20_260_925, home, away, Regulation, Rules);

        Describe(result).Should().Be(PinnedSequence);
    }

    [Fact]
    public void The_shootout_stops_once_one_side_cannot_be_caught()
    {
        var (home, away) = EvenSides();
        var stoppedEarly = 0;

        for (ulong seed = 1; seed <= 300; seed++)
        {
            var result = PenaltyShootout.Run(seed, home, away, Regulation, Rules);

            result.HomeScore.Should().NotBe(result.AwayScore);

            if (result.Kicks.Count >= 10)
            {
                continue;
            }

            stoppedEarly++;

            var loser = result.Winner == MatchSide.Home ? MatchSide.Away : MatchSide.Home;
            var loserKicks = result.Kicks.Count(kick => kick.Side == loser);
            var loserScore = loser == MatchSide.Home ? result.HomeScore : result.AwayScore;
            var winnerScore = loser == MatchSide.Home ? result.AwayScore : result.HomeScore;

            // The loser could not have drawn level with every kick he had left.
            winnerScore.Should().BeGreaterThan(loserScore + (Regulation - loserKicks));

            // And the shoot-out was not over a kick earlier: the same test failed one kick before the last.
            var beforeLast = result.Kicks.Take(result.Kicks.Count - 1).ToList();
            var winnerBefore = beforeLast.Count(kick => kick.Side == result.Winner && kick.IsGoal);
            var loserBefore = beforeLast.Count(kick => kick.Side == loser && kick.IsGoal);
            var winnerLeft = Regulation - beforeLast.Count(kick => kick.Side == result.Winner);
            var loserLeft = Regulation - beforeLast.Count(kick => kick.Side == loser);

            (winnerBefore > loserBefore + loserLeft || loserBefore > winnerBefore + winnerLeft).Should().BeFalse();
        }

        stoppedEarly.Should().BeGreaterThan(20, "a lopsided start is common enough to see in 300 shoot-outs");
    }

    [Fact]
    public void A_shootout_level_after_the_regulation_kicks_goes_to_sudden_death_in_whole_rounds()
    {
        var (home, away) = EvenSides();
        var suddenDeath = 0;

        for (ulong seed = 1; seed <= 300; seed++)
        {
            var result = PenaltyShootout.Run(seed, home, away, Regulation, Rules);

            if (result.Kicks.Count <= 10)
            {
                continue;
            }

            suddenDeath++;

            (result.Kicks.Count % 2).Should().Be(0);

            var afterRegulation = result.Kicks.Take(10).ToList();
            afterRegulation.Count(kick => kick.Side == MatchSide.Home && kick.IsGoal)
                .Should().Be(afterRegulation.Count(kick => kick.Side == MatchSide.Away && kick.IsGoal));

            // Each sudden-death round before the last was level, and the last was not.
            for (var round = Regulation + 1; round <= result.Kicks[^1].Round; round++)
            {
                var homeScored = result.Kicks.Single(kick => kick.Round == round && kick.Side == MatchSide.Home).IsGoal;
                var awayScored = result.Kicks.Single(kick => kick.Round == round && kick.Side == MatchSide.Away).IsGoal;

                (homeScored == awayScored).Should().Be(round < result.Kicks[^1].Round);
            }
        }

        suddenDeath.Should().BeGreaterThan(10);
    }

    [Fact]
    public void Sudden_death_cycles_round_the_order()
    {
        var (home, away) = EvenSides();
        home = home with { OnPitch = [.. home.OnPitch.Take(2)] };
        away = away with { OnPitch = [.. away.OnPitch.Take(2)] };

        ShootoutResult? longest = null;

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var result = PenaltyShootout.Run(seed, home, away, Regulation, Rules);

            if (longest is null || result.Kicks.Count > longest.Kicks.Count)
            {
                longest = result;
            }
        }

        longest!.Kicks.Count.Should().BeGreaterThan(10);

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var kicks = longest.Kicks.Where(kick => kick.Side == side).ToList();

            kicks.Select(kick => kick.TakerId).Distinct().Should().HaveCount(2);

            for (var index = 2; index < kicks.Count; index++)
            {
                kicks[index].TakerId.Should().Be(kicks[index - 2].TakerId);
            }
        }
    }

    [Fact]
    public void The_best_finishers_take_first_and_the_goalkeeper_last()
    {
        var input = TestMatchFactory.Even();
        var home = WithFinishing(input.Home, slot => 4 + slot);
        var away = WithFinishing(input.Away, slot => 20 - slot);

        var homeSide = ShootoutSide.FromFullTime(home, Lines(home));
        var awaySide = ShootoutSide.FromFullTime(away, Lines(away));

        var homeOrder = ExpectedOrder(home, descending: true);
        var awayOrder = ExpectedOrder(away, descending: true);

        var result = PenaltyShootout.Run(5, homeSide, awaySide, Regulation, Rules);

        // Three rounds are always played: the earliest a shoot-out can end is after the third round.
        TakersOf(result, MatchSide.Home).Take(3).Should().Equal(homeOrder.Take(3));
        TakersOf(result, MatchSide.Away).Take(3).Should().Equal(awayOrder.Take(3));

        homeOrder.Last().Should().Be(home.Starters().Single(player => player.IsGoalkeeper).ParticipantId);
        awayOrder.Last().Should().Be(away.Starters().Single(player => player.IsGoalkeeper).ParticipantId);
    }

    [Fact]
    public void Equal_finishers_are_ordered_by_identity()
    {
        var (home, away) = EvenSides();

        var result = PenaltyShootout.Run(3, home, away, Regulation, Rules);

        var expected = home.OnPitch
            .Where(slot => !slot.Participant.IsGoalkeeper)
            .Select(slot => slot.Participant.ParticipantId)
            .Order()
            .Take(3)
            .ToList();

        TakersOf(result, MatchSide.Home).Take(3).Should().Equal(expected);
    }

    [Fact]
    public void The_larger_side_drops_its_weakest_takers_to_match_the_smaller()
    {
        var input = TestMatchFactory.Even();
        var homeSide = ShootoutSide.FromFullTime(input.Home, Lines(input.Home));
        var awayLines = Lines(input.Away, line => line with { SentOff = line.ParticipantId.GetHashCode() % 5 == 0 });
        var awaySide = ShootoutSide.FromFullTime(input.Away, awayLines);

        awaySide.OnPitch.Count.Should().BeLessThan(homeSide.OnPitch.Count);

        var homeKeeper = homeSide.OnPitch.Single(slot => slot.Participant.IsGoalkeeper).Participant.ParticipantId;

        for (ulong seed = 1; seed <= 50; seed++)
        {
            var result = PenaltyShootout.Run(seed, homeSide, awaySide, Regulation, Rules);

            var homeTakers = TakersOf(result, MatchSide.Home);

            homeTakers.Distinct().Count().Should().BeLessThanOrEqualTo(awaySide.OnPitch.Count);

            if (awaySide.OnPitch.Count < homeSide.OnPitch.Count)
            {
                homeTakers.Should().NotContain(homeKeeper, "the goalkeeper is the weakest taker and goes first when a side is cut");
            }
        }
    }

    [Fact]
    public void A_side_without_a_goalkeeper_faces_no_goalkeeper_and_never_has_a_kick_saved()
    {
        var input = TestMatchFactory.Even();
        var awayNoKeeper = Lines(input.Away, line =>
            input.Away.Squad.Single(player => player.ParticipantId == line.ParticipantId).IsGoalkeeper
                ? line with { SentOff = true }
                : line);

        var homeSide = ShootoutSide.FromFullTime(input.Home, Lines(input.Home));
        var awaySide = ShootoutSide.FromFullTime(input.Away, awayNoKeeper);

        awaySide.OnPitch.Should().NotContain(slot => slot.Participant.IsGoalkeeper);

        var homeKicks = new List<ShootoutKick>();
        var awayKicks = new List<ShootoutKick>();

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var result = PenaltyShootout.Run(seed, homeSide, awaySide, Regulation, Rules);

            homeKicks.AddRange(result.Kicks.Where(kick => kick.Side == MatchSide.Home));
            awayKicks.AddRange(result.Kicks.Where(kick => kick.Side == MatchSide.Away));
        }

        homeKicks.Should().OnlyContain(kick => kick.GoalkeeperId == null && kick.Outcome != ShootoutKickOutcome.Saved);
        awayKicks.Should().OnlyContain(kick => kick.GoalkeeperId != null);
        awayKicks.Should().Contain(kick => kick.Outcome == ShootoutKickOutcome.Saved);
    }

    [Fact]
    public void A_better_side_wins_more_often_but_not_always()
    {
        // A 20 against a 1 is about 90% against 62% a kick: the better side wins most shoot-outs but not all.
        var input = TestMatchFactory.Build(1, homeAbility: 20, awayAbility: 1, new MatchInstructionsV1());
        var home = ShootoutSide.FromFullTime(input.Home, Lines(input.Home));
        var away = ShootoutSide.FromFullTime(input.Away, Lines(input.Away));

        var homeWins = Enumerable.Range(1, 1_000)
            .Count(seed => PenaltyShootout.Run((ulong)seed, home, away, Regulation, Rules).Winner == MatchSide.Home);

        homeWins.Should().BeGreaterThan(800).And.BeLessThan(1_000);
    }

    [Fact]
    public void Skill_moves_a_kick_more_than_an_open_play_penalty_but_stays_inside_its_bounds()
    {
        ActiveSlot Taker(int ability) => ShootoutSide.FromFullTime(
            TestMatchFactory.Build(1, ability, 13, new MatchInstructionsV1()).Home,
            Lines(TestMatchFactory.Build(1, ability, 13, new MatchInstructionsV1()).Home)).OnPitch.First(slot => !slot.Participant.IsGoalkeeper);

        ActiveSlot Keeper(int ability) => ShootoutSide.FromFullTime(
            TestMatchFactory.Build(1, 13, ability, new MatchInstructionsV1()).Away,
            Lines(TestMatchFactory.Build(1, 13, ability, new MatchInstructionsV1()).Away)).OnPitch.Single(slot => slot.Participant.IsGoalkeeper);

        var even = PenaltyShootout.KickChance(Taker(13), Keeper(13), Rules);
        var best = PenaltyShootout.KickChance(Taker(20), Keeper(6), Rules);
        var worst = PenaltyShootout.KickChance(Taker(6), Keeper(20), Rules);

        even.Should().BeInRange(7_500, 7_700);
        best.Should().BeGreaterThan(even + 800);
        worst.Should().BeLessThan(even - 800);
        best.Should().BeLessThanOrEqualTo(Rules.PenaltyMaxGoalBasisPoints);
        worst.Should().BeGreaterThanOrEqualTo(Rules.PenaltyMinGoalBasisPoints);
        PenaltyShootout.KickChance(Taker(13), null, Rules).Should().Be(Rules.PenaltyGoalBasisPoints);
    }

    [Fact]
    public void Full_time_players_are_those_who_started_or_came_on_and_were_not_taken_off()
    {
        var input = TestMatchFactory.Even();
        var side = input.Home;
        var starters = side.Starters();
        var bench = side.Squad.Where(player => starters.All(starter => starter.ParticipantId != player.ParticipantId)).ToList();

        var sentOff = starters[1].ParticipantId;
        var subbedOff = starters[2].ParticipantId;
        var hurtNoReplacement = starters[3].ParticipantId;
        var cameOn = bench[0].ParticipantId;
        var cameOnAndWasSubbedOff = bench[1].ParticipantId;
        var cameOnAndWasHurt = bench[2].ParticipantId;
        var unused = bench[3].ParticipantId;

        var lines = Lines(side, line =>
        {
            if (line.ParticipantId == sentOff)
            {
                return line with { SentOff = true };
            }

            if (line.ParticipantId == subbedOff)
            {
                return line with { SubbedOutMinute = 60 };
            }

            if (line.ParticipantId == hurtNoReplacement)
            {
                return line with { IsInjured = true, AbsenceFixtures = 2 };
            }

            if (line.ParticipantId == cameOn)
            {
                return line with { Started = false, SubbedInMinute = 60 };
            }

            if (line.ParticipantId == cameOnAndWasSubbedOff)
            {
                return line with { Started = false, SubbedInMinute = 50, SubbedOutMinute = 80 };
            }

            if (line.ParticipantId == cameOnAndWasHurt)
            {
                return line with { Started = false, SubbedInMinute = 50, IsInjured = true };
            }

            return line.ParticipantId == unused ? line with { Started = false } : line;
        });

        var onPitch = ShootoutSide.FromFullTime(side, lines).OnPitch.Select(slot => slot.Participant.ParticipantId).ToList();

        onPitch.Should().NotContain([sentOff, subbedOff, hurtNoReplacement, cameOnAndWasSubbedOff, cameOnAndWasHurt, unused]);
        onPitch.Should().Contain(cameOn);
        onPitch.Should().HaveCount(11 - 3 + 1);
    }

    [Fact]
    public void A_played_match_gives_eleven_players_a_side_unless_somebody_left()
    {
        var input = TestMatchFactory.Even();
        var result = TestMatchFactory.Play(input).Result;

        var home = ShootoutSide.FromFullTime(input.Home, result.PlayerLines);
        var away = ShootoutSide.FromFullTime(input.Away, result.PlayerLines);

        home.OnPitch.Count.Should().BeInRange(7, 11);
        away.OnPitch.Count.Should().BeInRange(7, 11);

        PenaltyShootout.Run(input.Seed, home, away, Regulation, Rules).Kicks.Should().NotBeEmpty();
    }

    [Fact]
    public void A_shootout_cannot_start_without_players()
    {
        var (home, _) = EvenSides();

        var act = () => PenaltyShootout.Run(1, home, home with { OnPitch = [] }, Regulation, Rules);

        act.Should().Throw<InvalidMatchInputException>();
    }

    [Fact]
    public void The_shootout_draws_from_a_stream_the_match_does_not_use()
    {
        PenaltyShootout.ShootoutStream.Should().NotBe(Randomness.Pcg32.DefaultStream);
    }

    private static (ShootoutSide Home, ShootoutSide Away) EvenSides()
    {
        var input = TestMatchFactory.Even();

        return (
            ShootoutSide.FromFullTime(input.Home, Lines(input.Home)),
            ShootoutSide.FromFullTime(input.Away, Lines(input.Away)));
    }

    /// <summary>The line of every starter, as if the eleven played all ninety minutes.</summary>
    private static List<MatchPlayerLineV1> Lines(MatchSideV1 side, Func<MatchPlayerLineV1, MatchPlayerLineV1>? change = null)
    {
        var starters = side.Starters().Select(player => player.ParticipantId).ToHashSet();

        return
        [
            .. side.Squad.Select(player =>
            {
                var line = new MatchPlayerLineV1
                {
                    ParticipantId = player.ParticipantId,
                    ClubId = side.ClubId,
                    Side = MatchSide.Home,
                    Started = starters.Contains(player.ParticipantId),
                    MinutesPlayed = starters.Contains(player.ParticipantId) ? 90 : 0,
                    Goals = 0,
                    Assists = 0,
                    YellowCards = 0,
                    SentOff = false,
                    AbsenceFixtures = 0,
                    RatingBasisPoints = 6_000,
                    FinalConditionBasisPoints = player.State.ConditionBasisPoints,
                };

                return change is null ? line : change(line);
            }),
        ];
    }

    private static MatchSideV1 WithFinishing(MatchSideV1 side, Func<int, int> finishingOfSlot)
    {
        var slotOf = side.Slots.ToDictionary(slot => slot.ParticipantId, slot => slot.SlotNumber);

        return side with
        {
            Squad =
            [
                .. side.Squad.Select(player =>
                {
                    if (!slotOf.TryGetValue(player.ParticipantId, out var slotNumber))
                    {
                        return player;
                    }

                    var values = player.Attributes.Values.ToArray();
                    values[(int)MatchAttributeName.Finishing] = int.Clamp(finishingOfSlot(slotNumber), 1, 20);

                    return player with { Attributes = PlayerAttributesV1.From(values) };
                }),
            ],
        };
    }

    private static List<Guid> ExpectedOrder(MatchSideV1 side, bool descending)
    {
        var starters = side.Starters();

        var outfield = starters.Where(player => !player.IsGoalkeeper);
        var ordered = (descending
                ? outfield.OrderByDescending(player => player.Attributes.ValueOf(MatchAttributeName.Finishing))
                : outfield.OrderBy(player => player.Attributes.ValueOf(MatchAttributeName.Finishing)))
            .ThenBy(player => player.ParticipantId)
            .Select(player => player.ParticipantId)
            .ToList();

        ordered.Add(starters.Single(player => player.IsGoalkeeper).ParticipantId);

        return ordered;
    }

    private static List<Guid> TakersOf(ShootoutResult result, MatchSide side) =>
        [.. result.Kicks.Where(kick => kick.Side == side).Select(kick => kick.TakerId)];

    /// <summary>Writes a shoot-out as who kicked first and each round's two outcomes: S scored, X saved, W wide.</summary>
    private static string Describe(ShootoutResult result)
    {
        var letters = result.Kicks.Select(kick => kick.Outcome switch
        {
            ShootoutKickOutcome.Scored => 'S',
            ShootoutKickOutcome.Saved => 'X',
            _ => 'W',
        });

        return $"{result.FirstSide} first {string.Concat(letters)} {result.HomeScore}-{result.AwayScore}";
    }

    private const string PinnedSequence = "Home first SSSXSWS 4-1";
}

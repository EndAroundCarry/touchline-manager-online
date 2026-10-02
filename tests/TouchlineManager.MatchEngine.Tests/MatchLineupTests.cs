using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The match center's lineups and the live condition and rating curve (`replay-v2`, master plan §9.5).
/// </summary>
/// <remarks>
/// The two presentation facts the replay adds beyond the keyframes. A lineup is a projection of the frozen
/// snapshot and the result's own player lines, so nothing in it may disagree with the result; the metrics
/// are captured from the run itself, so recording them may never change what the run produces.
/// </remarks>
public sealed class MatchLineupTests
{
    [Fact]
    public void A_lineup_names_every_player_and_marks_who_started()
    {
        for (var seed = 1UL; seed <= 10; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var presentation = TestMatchFactory.Play(input).Presentation;

            foreach (var (lineup, side) in new[]
            {
                (Lineup: presentation.HomeLineup!, Side: MatchSide.Home),
                (Lineup: presentation.AwayLineup!, Side: MatchSide.Away),
            })
            {
                var frozen = input.SideOf(side);

                lineup.ClubName.Should().Be(frozen.ClubName);
                lineup.ShortName.Should().Be(lineup.ShortName.ToUpperInvariant());
                lineup.ShortName.Should().NotBeEmpty();
                lineup.PrimaryColour.Should().StartWith("#");
                lineup.SecondaryColour.Should().StartWith("#");
                lineup.SecondaryColour.Should().NotBe(lineup.PrimaryColour);
                lineup.Formation.Should().NotBeNullOrWhiteSpace();

                lineup.Starters.Should().HaveCount(MatchInputV1.StartersOnPitch);
                lineup.Starters.Select(player => player.SlotNumber)
                    .Should().Equal(Enumerable.Range(1, MatchInputV1.StartersOnPitch));
                lineup.Starters.Should().OnlyContain(player => player.IsStarter);
                lineup.Starters.Select(player => player.ParticipantId)
                    .Should().BeEquivalentTo(frozen.Slots.Select(slot => slot.ParticipantId));

                lineup.Bench.Should().HaveCount(frozen.Squad.Count - MatchInputV1.StartersOnPitch);
                lineup.Bench.Should().OnlyContain(player => !player.IsStarter && player.SlotNumber == 0);

                // Everybody the snapshot named is on exactly one of the two lists.
                lineup.Starters.Concat(lineup.Bench)
                    .Select(player => player.ParticipantId)
                    .Should().BeEquivalentTo(frozen.Squad.Select(participant => participant.ParticipantId));
            }
        }
    }

    [Fact]
    public void A_lineup_carries_each_players_own_result_line()
    {
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var (result, presentation) = TestMatchFactory.Play(input);

            foreach (var (lineup, side) in new[]
            {
                (Lineup: presentation.HomeLineup!, Side: MatchSide.Home),
                (Lineup: presentation.AwayLineup!, Side: MatchSide.Away),
            })
            {
                var lines = result.PlayerLines
                    .Where(line => line.Side == side)
                    .ToDictionary(line => line.ParticipantId);

                foreach (var player in lineup.Starters.Concat(lineup.Bench))
                {
                    var line = lines[player.ParticipantId];

                    player.Goals.Should().Be(line.Goals);
                    player.Assists.Should().Be(line.Assists);
                    player.YellowCards.Should().Be(line.YellowCards);
                    player.SentOff.Should().Be(line.SentOff);
                    player.SubbedOutMinute.Should().Be(line.SubbedOutMinute);
                    player.SubbedInMinute.Should().Be(line.SubbedInMinute);
                    player.IsInjured.Should().Be(line.IsInjured);
                    player.FinalCondition.Should().Be(line.FinalConditionBasisPoints);
                    player.FinalRating.Should().Be(line.LiveRatingBasisPoints, "the panel shows the badge the replay watched fluctuate");
                    player.KickoffCondition.Should().BeGreaterThan(0);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(StandardShapes))]
    public void Every_standard_shape_is_named_exactly(string code, MatchRole[] roles)
    {
        var input = WithHomeSlots(roles);
        var result = MatchSimulator.Simulate(input);
        var lineup = MatchLineupBuilder.Build(input, result, MatchSide.Home);

        lineup.Formation.Should().Be(code);
    }

    public static TheoryData<string, MatchRole[]> StandardShapes()
    {
        var data = new TheoryData<string, MatchRole[]>();

        foreach (var (code, roles) in Shapes)
        {
            data.Add(code, roles);
        }

        return data;
    }

    [Fact]
    public void A_shape_that_matches_no_standard_is_described_by_the_lines_its_players_stand_in()
    {
        // A four-four-two whose right back was asked to play as a centre back: the role sequence matches no
        // standard shape, so the label comes from how deep the lines stand — which is still a four-four-two.
        var roles = Shapes[0].Roles.ToArray();
        roles[1] = MatchRole.CentreBack;

        var input = WithHomeSlots(roles);
        var result = MatchSimulator.Simulate(input);

        MatchLineupBuilder.Build(input, result, MatchSide.Home).Formation.Should().Be("4-4-2");
    }

    [Fact]
    public void Live_metrics_are_captured_for_every_player_on_the_pitch_at_every_minute()
    {
        var input = TestMatchFactory.Even();
        var (result, presentation) = TestMatchFactory.Play(input);

        presentation.LiveMetrics.Should().NotBeNull().And.NotBeEmpty();

        var squad = input.Home.Squad.Concat(input.Away.Squad)
            .ToDictionary(participant => participant.ParticipantId);

        foreach (var side in new[] { input.Home, input.Away })
        {
            var firsts = presentation.LiveMetrics!
                .Where(metric => side.Squad.Any(participant => participant.ParticipantId == metric.ParticipantId))
                .GroupBy(metric => metric.ParticipantId)
                .ToDictionary(group => group.Key, group => group.OrderBy(metric => metric.Minute).First());

            // The eleven that kicked off are on the pitch from the first minute, and nobody is fresher than
            // they were when the lock froze them: a bar that started higher would be a different match.
            side.Starters().Should().OnlyContain(starter =>
                firsts[starter.ParticipantId].Minute == 1
                && firsts[starter.ParticipantId].ConditionBasisPoints <= starter.State.ConditionBasisPoints);
        }

        presentation.LiveMetrics!.Should().OnlyContain(metric =>
            metric.Minute >= 1
            && metric.Minute <= result.TotalMinutesPlayed
            && squad.ContainsKey(metric.ParticipantId)
            && metric.ConditionBasisPoints > 0
            && metric.RatingBasisPoints > 0);

        // Every minute's capture is one row per player on the pitch, so the curve covers the whole match
        // rather than a sample of it.
        presentation.LiveMetrics!
            .GroupBy(metric => metric.Minute)
            .Should().OnlyContain(minute => minute.Select(metric => metric.ParticipantId).Distinct().Count() == minute.Count());
    }

    [Fact]
    public void A_player_who_left_the_pitch_has_no_metric_after_they_left()
    {
        // Substitutions are the one moment the panel's occupancy changes, and the curve is the evidence:
        // a bar that kept drawing after the player left would be a different player's condition.
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var recorder = new PlayerLiveMetricsRecorder();
            var result = MatchSimulator.Simulate(input, TestMatchFactory.Rules, recorder);

            foreach (var line in result.PlayerLines.Where(line => line.SubbedOutMinute is not null))
            {
                var last = recorder.Metrics
                    .Where(metric => metric.ParticipantId == line.ParticipantId)
                    .Select(metric => (int?)metric.Minute)
                    .Max();

                last.Should().BeLessOrEqualTo(line.SubbedOutMinute!.Value);
            }
        }
    }

    [Fact]
    public void A_full_match_ends_at_the_condition_and_rating_its_line_records()
    {
        var input = TestMatchFactory.Even();
        var recorder = new PlayerLiveMetricsRecorder();
        var result = MatchSimulator.Simulate(input, TestMatchFactory.Rules, recorder);

        foreach (var line in result.PlayerLines.Where(line =>
            line.Started && line.SubbedOutMinute is null && !line.SentOff && line.MinutesPlayed > 0))
        {
            var last = recorder.Metrics
                .Where(metric => metric.ParticipantId == line.ParticipantId)
                .OrderBy(metric => metric.Minute)
                .Last();

            last.ConditionBasisPoints.Should().Be(line.FinalConditionBasisPoints, "the bar starts where it ended");
            last.RatingBasisPoints.Should().Be(line.LiveRatingBasisPoints, "the badge ends where the result says");
        }
    }

    [Fact]
    public void Recording_the_metrics_does_not_change_the_result()
    {
        // The reason the curve is a recorder rather than part of the result: a replay read re-simulates the
        // same frozen input, and the hash check must still prove it is the published match (MAT-8, MAT-9).
        var input = TestMatchFactory.Even();

        var plain = MatchSimulator.Simulate(input);
        var recorded = MatchSimulator.Simulate(input, TestMatchFactory.Rules, new PlayerLiveMetricsRecorder());

        recorded.Should().BeEquivalentTo(plain, options => options.WithStrictOrdering());
        recorded.OutputHash.Should().Be(plain.OutputHash);
        recorded.InputHash.Should().Be(plain.InputHash);
    }

    [Fact]
    public void The_metrics_are_deterministic()
    {
        var input = TestMatchFactory.Even();

        var first = new PlayerLiveMetricsRecorder();
        var second = new PlayerLiveMetricsRecorder();

        MatchSimulator.Simulate(input, TestMatchFactory.Rules, first);
        MatchSimulator.Simulate(input, TestMatchFactory.Rules, second);

        first.Metrics.Should().BeEquivalentTo(second.Metrics, options => options.WithStrictOrdering());
    }

    /// <summary>The six standard shapes, as the role each slot asks for in slot order.</summary>
    private static readonly (string Code, MatchRole[] Roles)[] Shapes =
    [
        ("4-4-2",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.Winger,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.Striker,
            MatchRole.Striker,
        ]),
        ("4-3-3",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.DefensiveMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.Striker,
            MatchRole.Winger,
        ]),
        ("4-2-3-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.DefensiveMidfielder,
            MatchRole.DefensiveMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.Striker,
        ]),
        ("4-1-4-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.DefensiveMidfielder,
            MatchRole.Winger,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.Striker,
        ]),
        ("3-5-2",
        [
            MatchRole.Goalkeeper,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.WingBack,
            MatchRole.CentralMidfielder,
            MatchRole.DefensiveMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.WingBack,
            MatchRole.Striker,
            MatchRole.Striker,
        ]),
        ("5-3-2",
        [
            MatchRole.Goalkeeper,
            MatchRole.WingBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.WingBack,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Striker,
            MatchRole.Striker,
        ]),
    ];

    /// <summary>
    /// Builds the factory's snapshot with the home side's slots re-roled into one shape.
    /// </summary>
    /// <param name="roles">The role each slot 1…11 asks for.</param>
    /// <remarks>
    /// The depth each family stands at comes from the standard layouts' own geometry — the keeper on his
    /// line, the defenders deepest, the front line highest — so the non-standard fallback measures a real
    /// shape rather than the factory's across-the-pitch coordinates.
    /// </remarks>
    private static MatchInputV1 WithHomeSlots(MatchRole[] roles)
    {
        var input = TestMatchFactory.Even();
        var home = input.Home;

        var slots = home.Slots
            .OrderBy(slot => slot.SlotNumber)
            .Select((slot, index) =>
            {
                var role = roles[index];

                return slot with
                {
                    Role = role,
                    Family = role.FamilyOf(),
                    X = DepthOf(role),
                    Y = YOf(index),
                };
            })
            .ToList();

        return input with { Home = home with { Slots = slots } };
    }

    /// <summary>
    /// How deep up the pitch a role stands, following the standard layouts rather than the families.
    /// </summary>
    /// <remarks>
    /// A winger is an attacker by family but stands in midfield in a four-four-two, and a wing back is a
    /// defender by family but stands in midfield in a three-five-two — so the depth has to come from the
    /// role for the fallback's lines to describe what a viewer would see.
    /// </remarks>
    private static int DepthOf(MatchRole role) => role switch
    {
        MatchRole.Goalkeeper => 500,
        MatchRole.CentreBack or MatchRole.FullBack => 1_900,
        MatchRole.WingBack or MatchRole.DefensiveMidfielder => 4_200,
        MatchRole.CentralMidfielder => 5_400,
        MatchRole.Winger => 5_800,
        MatchRole.AttackingMidfielder => 6_400,
        _ => 8_200,
    };

    private static int YOf(int index) => index switch
    {
        0 => 5_000,
        1 => 8_000,
        2 => 6_000,
        3 => 4_000,
        4 => 2_000,
        5 => 8_300,
        6 => 6_200,
        7 => 3_800,
        8 => 1_700,
        9 => 6_800,
        _ => 3_200,
    };
}

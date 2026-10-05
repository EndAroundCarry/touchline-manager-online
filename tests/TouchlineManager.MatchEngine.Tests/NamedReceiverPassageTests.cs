using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// What a played match records of the receiver chain (`engine-v10`): a named receiver for each pass of the approach,
/// who plays it on, and who the film shows receiving it.
/// </summary>
public sealed class NamedReceiverPassageTests
{
    private const ulong Seeds = 12;

    [Fact]
    public void Every_ball_that_is_received_is_received_by_a_player_of_the_side_that_had_it()
    {
        var received = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = PassageTestHelpers.Play(seed);

            foreach (var passage in match.Passages)
            {
                var side = SquadOf(match.Input, passage.Side);

                foreach (var touch in passage.Touches.Where(touch => touch.Action == PassageAction.Receive))
                {
                    side.Should().Contain(touch.ParticipantId);
                    received++;
                }
            }
        }

        received.Should().BeGreaterThan(1_000, "a possession is played through named receivers");
    }

    [Fact]
    public void A_man_who_is_given_the_ball_plays_it_on_himself()
    {
        var pairs = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            foreach (var passage in PassageTestHelpers.Play(seed).Passages)
            {
                for (var index = 0; index + 1 < passage.Touches.Count; index++)
                {
                    var touch = passage.Touches[index];
                    var next = passage.Touches[index + 1];

                    if (touch.Action == PassageAction.Receive && next.Action is PassageAction.Pass or PassageAction.Cross)
                    {
                        next.ParticipantId.Should().Be(touch.ParticipantId);
                        pairs++;
                    }
                }
            }
        }

        pairs.Should().BeGreaterThan(500);
    }

    [Fact]
    public void A_pass_is_played_by_somebody_other_than_the_man_it_was_played_to()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            foreach (var passage in PassageTestHelpers.Play(seed).Passages)
            {
                Guid? passer = null;

                foreach (var touch in passage.Touches)
                {
                    switch (touch.Action)
                    {
                        case PassageAction.Pass or PassageAction.Cross:
                            passer = touch.ParticipantId;
                            break;

                        case PassageAction.Receive:
                            (touch.ParticipantId == passer).Should().BeFalse("nobody passes to himself");
                            passer = null;
                            break;

                        default:
                            break;
                    }
                }
            }
        }
    }

    [Fact]
    public void No_defender_is_given_the_ball_beyond_the_halfway_line()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var match = PassageTestHelpers.Play(seed);
            var defenders = match.Input.Home.Slots.Concat(match.Input.Away.Slots)
                .Where(slot => slot.Family == MatchPositionFamily.Defence)
                .Select(slot => slot.ParticipantId)
                .ToHashSet();

            foreach (var passage in match.Passages)
            {
                foreach (var touch in passage.Touches.Where(touch => touch.Action == PassageAction.Receive && defenders.Contains(touch.ParticipantId)))
                {
                    PassageTestHelpers.AttackingX(touch.X, passage.Side)
                        .Should().BeLessThanOrEqualTo(EngineRulesV2.Default.DefenderReceiveMaxPointX);
                }
            }
        }
    }

    [Fact]
    public void The_film_shows_the_player_the_engine_named_receiving_the_ball()
    {
        var named = 0;
        var missing = 0;

        for (var seed = 1UL; seed <= 6; seed++)
        {
            var (_, passages, script) = TestMatchFactory.Script(TestMatchFactory.Even(seed));

            foreach (var possession in script.Possessions)
            {
                var shown = script.Beats
                    .Skip(possession.FirstBeat)
                    .Take(possession.LastBeat - possession.FirstBeat + 1)
                    .Where(beat => beat.Receiver is not null)
                    .Select(beat => beat.Receiver!.Value)
                    .ToHashSet();

                foreach (var touch in passages[possession.Index].Touches.Where(touch => touch.Action == PassageAction.Receive))
                {
                    named++;
                    missing += shown.Contains(touch.ParticipantId) ? 0 : 1;
                }
            }
        }

        named.Should().BeGreaterThan(2_000);

        // A leg shorter than the film's smallest move is not shown at all, and the film does not replace the engine's
        // receiver with somebody else: that is all that may be missing.
        missing.Should().BeLessThan(named / 200, "the film shows the engine's receiver");
    }

    [Fact]
    public void Passes_are_credited_to_the_players_who_played_them_so_a_side_adds_up_to_what_it_attempted()
    {
        for (var seed = 1UL; seed <= 6; seed++)
        {
            var match = PassageTestHelpers.Play(seed);

            foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
            {
                var squad = SquadOf(match.Input, side);
                var lines = match.Result.PlayerLines.Where(line => squad.Contains(line.ParticipantId)).ToList();

                lines.Sum(line => line.PassesCompleted).Should().BeLessThanOrEqualTo(lines.Sum(line => line.PassesAttempted));
                lines.Sum(line => line.PassesAttempted).Should().BeGreaterThan(150);
                lines.Where(line => line.PassesAttempted > 0).Should().OnlyContain(line => squad.Contains(line.ParticipantId));
            }
        }
    }

    private static HashSet<Guid> SquadOf(MatchInputV1 input, MatchSide side) =>
        [.. input.SideOf(side).Squad.Select(participant => participant.ParticipantId)];
}

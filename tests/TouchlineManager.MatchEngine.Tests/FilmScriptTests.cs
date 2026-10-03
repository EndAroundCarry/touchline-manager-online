using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The film script (`replay-v4`): what each possession is turned into before it is timed or moved — who plays,
/// where a cross is drawn, who takes a restart, and whether the beats join up.
/// </summary>
public sealed class FilmScriptTests
{
    private const int Seeds = 16;

    [Fact]
    public void A_cross_is_drawn_only_from_a_wide_final_third_position_or_a_set_piece_and_always_into_the_box()
    {
        var crosses = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.Even(seed));

            for (var index = 0; index < script.Beats.Count; index++)
            {
                var beat = script.Beats[index];

                if (beat.Kind != BeatKind.Cross)
                {
                    continue;
                }

                var fromSetPiece = index > 0 && script.Beats[index - 1].Hold is HoldKind.Corner or HoldKind.FreeKick;
                var along = FilmSpace.Attacking(beat.From, beat.Side);
                var wide = Math.Abs(beat.From.Y - (FilmSpace.Width / 2)) >= 16.0 && along >= FilmSpace.Length * 2.0 / 3.0;

                (fromSetPiece || wide).Should().BeTrue("anything that is not a wide ball into the box is a lofted pass");
                FilmSpace.Attacking(beat.To, beat.Side).Should().BeGreaterThanOrEqualTo(FilmSpace.Length - 16.5);
                Math.Abs(beat.To.Y - (FilmSpace.Width / 2)).Should().BeLessThanOrEqualTo(20.2);
                crosses++;
            }
        }

        crosses.Should().BeGreaterThan(20);
    }

    [Fact]
    public void Consecutive_beats_join_except_at_a_cut()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.Even(seed));

            for (var index = 1; index < script.Beats.Count; index++)
            {
                var previous = script.Beats[index - 1];
                var beat = script.Beats[index];

                if (beat.Cut)
                {
                    continue;
                }

                beat.From.DistanceTo(previous.To).Should().BeLessThanOrEqualTo(
                    1.0,
                    $"beat {index} ({beat.Kind}) begins where beat {index - 1} ({previous.Kind}) left the ball");
            }
        }
    }

    [Fact]
    public void Every_possession_is_scripted_and_owns_a_contiguous_run_of_beats()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, passages, script) = TestMatchFactory.Script(TestMatchFactory.Even(seed));

            script.Possessions.Should().HaveCount(passages.Count);

            for (var index = 0; index < script.Possessions.Count; index++)
            {
                var possession = script.Possessions[index];

                possession.LastBeat.Should().BeGreaterThanOrEqualTo(possession.FirstBeat);

                if (index > 0)
                {
                    possession.FirstBeat.Should().Be(script.Possessions[index - 1].LastBeat + 1);
                }
            }
        }
    }

    [Fact]
    public void A_restart_is_taken_by_the_side_that_owns_it_and_a_goal_kick_by_its_goalkeeper()
    {
        var restarts = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var (_, _, script) = TestMatchFactory.Script(input);

            foreach (var possession in script.Possessions.Where(candidate => candidate.Source.Restart != PassageRestartKind.None))
            {
                var hold = script.Beats
                    .Skip(possession.FirstBeat)
                    .Take(possession.LastBeat - possession.FirstBeat + 1)
                    .First(beat => beat.IsHold && beat.Hold is HoldKind.KickOff or HoldKind.GoalKick or HoldKind.KeeperBall or HoldKind.FreeKick);

                hold.Side.Should().Be(possession.Source.Side, "a restart belongs to somebody (MAT-12)");

                if (possession.Source.Restart is PassageRestartKind.GoalKick or PassageRestartKind.KeeperBall && hold.Actor is Guid taker)
                {
                    input.SideOf(possession.Source.Side).Squad
                        .Single(participant => participant.ParticipantId == taker)
                        .IsGoalkeeper.Should().BeTrue("the goalkeeper takes a goal kick and a keeper's ball");
                }

                restarts++;
            }
        }

        restarts.Should().BeGreaterThan(50);
    }

    [Fact]
    public void The_players_the_engine_named_are_at_their_beats()
    {
        var checkedTouches = 0;

        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.Even(seed));

            foreach (var possession in script.Possessions)
            {
                var beats = script.Beats
                    .Skip(possession.FirstBeat)
                    .Take(possession.LastBeat - possession.FirstBeat + 1)
                    .ToList();

                foreach (var touch in possession.Source.Touches)
                {
                    var named = touch.ParticipantId;

                    var present = touch.Action switch
                    {
                        PassageAction.Shot => beats.Any(beat => beat.Kind == BeatKind.Shot && beat.Actor == named),
                        PassageAction.Save => beats.Any(beat => beat.Kind == BeatKind.Save && beat.Actor == named),
                        PassageAction.Header => beats.Any(beat => beat.Kind == BeatKind.Header && beat.Actor == named),
                        PassageAction.Penalty or PassageAction.FreeKick => beats.Any(beat => beat.IsHold && beat.Actor == named),
                        _ => (bool?)null,
                    };

                    if (present is null)
                    {
                        continue;
                    }

                    Assert.True(
                        present.Value,
                        $"seed {seed}, possession {possession.Index} ({possession.Source.Outcome}): the {touch.Action} by {named} is not a beat "
                        + $"({string.Join(", ", beats.Select(beat => beat.IsHold ? beat.Hold.ToString() : beat.Kind.ToString()))})");

                    checkedTouches++;
                }
            }
        }

        checkedTouches.Should().BeGreaterThan(100);
    }

    [Fact]
    public void A_goal_is_followed_by_its_celebration_and_the_kick_off_after_it_is_a_cut()
    {
        for (var seed = 1UL; seed <= Seeds; seed++)
        {
            var (_, _, script) = TestMatchFactory.Script(TestMatchFactory.Even(seed));

            for (var index = 0; index + 1 < script.Beats.Count; index++)
            {
                if (script.Beats[index].Hold != HoldKind.Goal)
                {
                    continue;
                }

                script.Beats[index].Formation.Should().Be(FormationMode.Celebration);
                script.Beats[index].HoldFilmSeconds.Should().Be(new HighlightOptionsV1().GoalHoldSeconds);

                var next = script.Beats[index + 1];

                next.Cut.Should().BeTrue("nobody could run from the net to the centre spot");
                next.CutKind.Should().BeOneOf("kick_off", "half_time");
            }
        }
    }
}

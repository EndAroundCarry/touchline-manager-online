using FluentAssertions;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>Verifies which lines a tick film's banner shows and when: one for every action, none thinned away, a dwell on the key ones.</summary>
public sealed class TickCommentaryTests
{
    private static readonly HighlightOptionsV1 Options = new();

    private static readonly string[] KeyBuildKeys =
    [
        "match.build.chance",
        "match.build.shot_long",
        "match.build.save",
        "match.build.save_held",
        "match.build.save_parried",
        "match.build.save_tipped",
        "match.build.smother",
        "match.build.cross",
        "match.build.block",
        "match.build.header",
    ];

    private static readonly string[] ShotOutcomeKeys =
    [
        "match.build.save_held",
        "match.build.save_parried",
        "match.build.save_tipped",
        "match.build.block",
        "match.shot.saved",
        "match.shot.blocked",
        "match.shot.off_target",
        "match.shot.woodwork",
        "match.goal",
        "match.penalty.goal",
        "match.penalty.missed",
    ];

    private static readonly Lazy<MatchPresentationV1> Even = new(() => Film(TickPlay.Even));
    private static readonly Lazy<MatchPresentationV1> Reduced = new(() => Film(TickPlay.Reduced));

    private static MatchPresentationV1 Film(TickSample sample) =>
        ReplayDirector.Build(sample.Input, sample.Result, sample.Recorder, Options, sample.Metrics.Metrics);

    /// <summary>A line is a key line when it is an event's, or a shot, a save, a block, a header or a cross.</summary>
    private static bool IsKey(HighlightCommentaryV1 line) =>
        !line.TemplateKey.StartsWith("match.build.", StringComparison.Ordinal)
            && !line.TemplateKey.StartsWith("match.restart.", StringComparison.Ordinal)
            || KeyBuildKeys.Contains(line.TemplateKey);

    private static IEnumerable<PassageV1> Played(MatchPresentationV1 film) => film.Passages.Where(passage => passage.OutcomeCode != "half_time");

    [Fact]
    public void The_film_is_made_by_the_v4_synthesizer_and_the_v6_words()
    {
        TickReplaySynthesizer.Version.Should().Be("tick-replay-v4");
        Even.Value.PresentationVersion.Should().Be("tick-replay-v4");
    }

    [Fact]
    public void Lines_are_far_more_frequent_than_the_old_two_and_a_half_second_gap_allowed()
    {
        var film = Even.Value;
        var lines = Played(film).Sum(passage => passage.Commentary.Count);
        var minutes = Played(film).Sum(passage => passage.DurationMilliseconds) / 60_000.0;

        (lines / minutes).Should().BeGreaterThan(12, "every action starts a line, where the old gap allowed at most 24 a film minute and kept far fewer");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_routine_line_falls_inside_the_dwell_of_a_key_line(bool reduced)
    {
        foreach (var passage in Played(reduced ? Reduced.Value : Even.Value))
        {
            var lastKey = int.MinValue / 2;

            foreach (var line in passage.Commentary)
            {
                if (IsKey(line))
                {
                    lastKey = line.TimeMilliseconds;

                    continue;
                }

                var sinceKey = line.TimeMilliseconds - lastKey;

                (sinceKey <= 0 || sinceKey >= Options.CommentaryKeyDwellMilliseconds)
                    .Should().BeTrue($"{line.TemplateKey} at {line.TimeMilliseconds} ms follows a key line by {sinceKey} ms");
            }
        }
    }

    [Fact]
    public void Two_lines_are_never_closer_than_the_minimum_gap()
    {
        foreach (var passage in Played(Even.Value))
        {
            for (var index = 1; index < passage.Commentary.Count; index++)
            {
                var previous = passage.Commentary[index - 1];
                var line = passage.Commentary[index];

                // Lines the end of the passage clamps together are one instant, and the opener is alone at zero.
                if (line.TimeMilliseconds >= passage.DurationMilliseconds)
                {
                    continue;
                }

                (line.TimeMilliseconds - previous.TimeMilliseconds).Should().BeGreaterThanOrEqualTo(Options.CommentaryMinGapMilliseconds, line.Text);
            }
        }
    }

    [Fact]
    public void A_shots_outcome_never_covers_the_line_that_says_it_was_struck()
    {
        var pairs = 0;

        foreach (var passage in Played(Even.Value))
        {
            for (var index = 1; index < passage.Commentary.Count; index++)
            {
                var strike = passage.Commentary[index - 1];
                var outcome = passage.Commentary[index];

                if (strike.TemplateKey is "match.build.chance" or "match.build.shot_long" && ShotOutcomeKeys.Contains(outcome.TemplateKey))
                {
                    pairs++;

                    if (outcome.TimeMilliseconds < passage.DurationMilliseconds)
                    {
                        (outcome.TimeMilliseconds - strike.TimeMilliseconds).Should().BeGreaterThanOrEqualTo(Options.CommentaryStrikeHoldMilliseconds);
                    }
                }
            }
        }

        pairs.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_passage_the_film_cut_to_opens_with_a_line_so_none_from_before_the_cut_is_left_up(bool reduced)
    {
        var cut = Played(reduced ? Reduced.Value : Even.Value).Where(passage => passage.Cuts.Count > 0).ToList();

        cut.Should().NotBeEmpty();

        foreach (var passage in cut)
        {
            passage.Commentary.Should().NotBeEmpty();
            passage.Commentary[0].TimeMilliseconds.Should().BeLessThan(Options.CommentaryOpenerWithinMilliseconds, $"passage at match second {passage.StartMatchSecond}");
        }
    }

    [Fact]
    public void A_pass_is_told_when_it_is_kicked_and_names_the_man_it_is_for()
    {
        var passes = Played(Even.Value)
            .SelectMany(passage => passage.Commentary)
            .Where(line => line.TemplateKey is "match.build.pass" or "match.build.through_ball" or "match.build.long_pass" or "match.build.cross")
            .ToList();

        passes.Should().HaveCountGreaterThan(50);
        passes.Count(line => line.Parameters.Any(parameter => parameter.Name == "secondId"))
            .Should().BeGreaterThan(passes.Count * 8 / 10, "the stamp names the receiver, so almost every pass line does");
    }

    [Fact]
    public void A_save_says_how_the_keeper_stopped_it_and_a_tackle_names_both_men()
    {
        var keys = Played(Even.Value).SelectMany(passage => passage.Commentary).Select(line => line.TemplateKey).ToHashSet();

        keys.Should().Contain("match.build.take_on");
        keys.Should().Contain(key => key == "match.build.tackled" || key == "match.build.poked_away");
        keys.Should().Contain(key => key.StartsWith("match.build.save_", StringComparison.Ordinal) || key == "match.build.smother" || key == "match.shot.saved");

        foreach (var line in Played(Even.Value).SelectMany(passage => passage.Commentary).Where(line => line.TemplateKey == "match.build.tackled"))
        {
            line.Parameters.Should().Contain(parameter => parameter.Name == "secondId");
        }
    }

    [Fact]
    public void Every_line_is_filled_in()
    {
        foreach (var line in Played(Even.Value).SelectMany(passage => passage.Commentary))
        {
            line.Text.Should().NotContain("{").And.NotContain("}").And.NotBeEmpty();
        }
    }

    [Fact]
    public void Every_family_renders_every_variant_with_no_placeholder_left_and_never_invents_a_team_mate()
    {
        var input = TickPlay.Even.Input;
        var first = input.Home.Squad[0].ParticipantId;
        var second = input.Home.Squad[1].ParticipantId;
        var kinds = Enum.GetValues<PassageBeatKind>().Where(kind => kind != PassageBeatKind.Event).ToList();

        foreach (var kind in kinds)
        {
            var named = Enumerable.Range(0, 12).Select(seed => new PassageBeatV1(0, MatchSide.Home, first, kind, null, seed, second)).ToList();
            var alone = Enumerable.Range(0, 12).Select(seed => new PassageBeatV1(0, MatchSide.Home, first, kind, null, seed)).ToList();

            var withSecond = CommentaryTokenBuilder.BuildPassageCommentary(input, named, 1_000);
            var without = CommentaryTokenBuilder.BuildPassageCommentary(input, alone, 1_000);

            withSecond.Should().HaveCount(named.Count, $"{kind} has a family");
            without.Should().HaveCount(alone.Count);
            withSecond.Select(line => line.Text).Should().OnlyContain(text => !text.Contains('{') && !text.Contains('}') && text.Length > 0, kind.ToString());

            if (kind != PassageBeatKind.Skipped)
            {
                // The possession engine's "skipped" always names the man left behind.
                without.Select(line => line.Text).Should().OnlyContain(text => !text.Contains("a team-mate"), $"{kind} has a variant that needs no second man");
            }
        }
    }

    [Fact]
    public void Every_build_up_line_is_short_enough_for_one_line_of_the_banner_at_phone_width()
    {
        var input = TickPlay.Even.Input;
        var first = input.Home.Squad[0].ParticipantId;
        var second = input.Home.Squad[1].ParticipantId;

        // The families only the possession engine's film still uses keep their old, longer lines.
        PassageBeatKind[] possessionOnly = [PassageBeatKind.Event, PassageBeatKind.Carry, PassageBeatKind.Dribble, PassageBeatKind.Tackle, PassageBeatKind.Interception, PassageBeatKind.Skipped];
        var kinds = Enum.GetValues<PassageBeatKind>().Except(possessionOnly);

        foreach (var kind in kinds)
        {
            var beats = Enumerable.Range(0, 24).Select(seed => new PassageBeatV1(0, MatchSide.Home, first, kind, null, seed, second)).ToList();

            foreach (var line in CommentaryTokenBuilder.BuildPassageCommentary(input, beats, 1_000))
            {
                // About eight words, and a club or a name can take a word or two more than a placeholder does.
                line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.Should().BeLessThanOrEqualTo(11, line.VariantKey);
                line.Text.Length.Should().BeLessThanOrEqualTo(64, line.VariantKey);
            }
        }
    }

    [Fact]
    public void A_goal_line_can_say_the_keeper_was_beaten()
    {
        var goals = new List<CommentaryToken>();

        for (var seed = 1UL; seed <= 12; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var result = MatchSimulator.Simulate(input);

            goals.AddRange(CommentaryTokenBuilder.Build(input, result).Where(token => token.TemplateKey == "match.goal"));
        }

        goals.Should().NotBeEmpty();
        goals.Should().Contain(token => token.Text.Contains("is beaten by") || token.Text.Contains("scores past") || token.Text.Contains("slots it past") || token.Text.Contains(" beats "));
    }
}

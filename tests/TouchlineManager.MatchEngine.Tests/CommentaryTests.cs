using FluentAssertions;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Commentary is built from events, cannot change them, and reveals nothing it should not (MAT-8, MAT-11).
/// </summary>
public sealed class CommentaryTests
{
    [Fact]
    public void Every_narratable_event_gets_a_line()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        var tokens = CommentaryTokenBuilder.Build(input, result);

        tokens.Should().NotBeEmpty();
        tokens.Should().HaveCountLessOrEqualTo(result.Events.Count);

        tokens.Select(token => token.EventSequence).Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_lines_are_ordered_by_event_sequence()
    {
        var input = TestMatchFactory.Even(seed: 4_242);
        var result = MatchSimulator.Simulate(input);

        CommentaryTokenBuilder.Build(input, result)
            .Select(token => token.EventSequence)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_same_match_produces_the_same_words()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        var first = CommentaryTokenBuilder.Build(input, result);
        var second = CommentaryTokenBuilder.Build(input, result);

        first.Should().BeEquivalentTo(second, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Every_template_uses_more_than_one_variant_across_a_season_of_matches()
    {
        // Repetition is what makes commentary read as machine output. The variants are chosen by event sequence,
        // so this asserts that a given template key really does produce different sentences somewhere.
        var variants = new Dictionary<string, HashSet<string>>();

        for (var seed = 1UL; seed <= 30; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var result = MatchSimulator.Simulate(input);

            foreach (var token in CommentaryTokenBuilder.Build(input, result))
            {
                if (!variants.TryGetValue(token.TemplateKey, out var seen))
                {
                    seen = [];
                    variants[token.TemplateKey] = seen;
                }

                seen.Add(token.VariantKey);
            }
        }

        variants.Should().NotBeEmpty();
        variants.Values.Count(seen => seen.Count > 1)
            .Should().BeGreaterThan(variants.Count / 2, "most templates should rotate their wording");
    }

    [Fact]
    public void No_line_carries_a_hidden_value()
    {
        // An allowlist rather than a blocklist: a parameter that is not a fact a manager can already see has no
        // business on the wire, and a new one should have to be added here deliberately.
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "absenceFixtures",
            "club",
            "clubId",
            "opponent",
            "playerId",
            "second",
            "secondId",
            "shotZone",
        };

        for (var seed = 1UL; seed <= 12; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var result = MatchSimulator.Simulate(input);

            foreach (var token in CommentaryTokenBuilder.Build(input, result))
            {
                foreach (var parameter in token.Parameters)
                {
                    allowed.Should().Contain(parameter.Name, "MAT-11 forbids anything else reaching a manager");
                }

                token.Text.Should().NotContain("Potential", "MAT-11 forbids hidden values in prose either");
                token.Text.Should().NotContain("Reputation");
                token.Text.Should().NotContain("{", "an unfilled placeholder is a bug a manager would read");
                token.Text.Should().NotBeEmpty();
            }
        }
    }

    [Fact]
    public void A_goal_names_the_scorer_and_the_club()
    {
        var input = TestMatchFactory.Even();

        // A seed chosen because the laboratory showed this fixture has goals in it.
        var result = MatchSimulator.Simulate(input);
        var goals = result.Events.Where(matchEvent => matchEvent.IsGoal).ToList();

        goals.Should().NotBeEmpty("this fixture is a scoring one");

        var tokens = CommentaryTokenBuilder.Build(input, result);

        foreach (var goal in goals)
        {
            var token = tokens.Single(candidate => candidate.EventSequence == goal.Sequence);
            var scorer = input.SideOf(goal.Side).Squad
                .Single(participant => participant.ParticipantId == goal.ParticipantId);

            token.Text.Should().Contain(scorer.DisplayName);
        }
    }

    [Fact]
    public void A_substitution_names_both_players()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        var substitutions = result.Events
            .Where(matchEvent => matchEvent.Type == EngineEventType.Substitution)
            .ToList();

        substitutions.Should().NotBeEmpty("the laboratory showed this fixture uses its bench");

        var tokens = CommentaryTokenBuilder.Build(input, result);

        foreach (var substitution in substitutions)
        {
            var token = tokens.Single(candidate => candidate.EventSequence == substitution.Sequence);
            var goingOff = input.SideOf(substitution.Side).Squad
                .Single(participant => participant.ParticipantId == substitution.ParticipantId);
            var comingOn = input.SideOf(substitution.Side).Squad
                .Single(participant => participant.ParticipantId == substitution.SecondaryParticipantId);

            token.Text.Should().Contain(goingOff.DisplayName);
            token.Text.Should().Contain(comingOn.DisplayName);
        }
    }

    [Fact]
    public void The_clock_reads_as_football_reads_it()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        var tokens = CommentaryTokenBuilder.Build(input, result);

        tokens.Should().OnlyContain(token => token.StoppageMinute >= 0);
        tokens.Should().OnlyContain(token => token.Minute >= 1 && token.Minute <= 90);
    }
}

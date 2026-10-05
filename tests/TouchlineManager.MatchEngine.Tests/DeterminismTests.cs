using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Serialization;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The engine's central promise: the same frozen input always produces the same result (MAT-9, ADR-0004).
/// </summary>
public sealed class DeterminismTests
{
    [Fact]
    public void The_same_snapshot_produces_the_same_output_hash_twice()
    {
        var input = TestMatchFactory.Even();

        var first = MatchSimulator.Simulate(input);
        var second = MatchSimulator.Simulate(input);

        second.OutputHash.Should().Be(first.OutputHash);
        second.HomeGoals.Should().Be(first.HomeGoals);
        second.Events.Count.Should().Be(first.Events.Count);
    }

    [Fact]
    public void The_same_snapshot_produces_byte_identical_canonical_output()
    {
        var input = TestMatchFactory.Even();

        CanonicalMatchSerializer.CanonicalText(MatchSimulator.Simulate(input))
            .Should().Be(CanonicalMatchSerializer.CanonicalText(MatchSimulator.Simulate(input)));
    }

    [Fact]
    public void A_twenty_times_simulation_is_still_the_same_result()
    {
        var input = TestMatchFactory.Even();
        var expected = MatchSimulator.Simulate(input).OutputHash;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            MatchSimulator.Simulate(input).OutputHash.Should().Be(expected);
        }
    }

    [Fact]
    public void A_different_seed_changes_the_result_without_breaking_it()
    {
        var first = MatchSimulator.Simulate(TestMatchFactory.Even(seed: 1));
        var second = MatchSimulator.Simulate(TestMatchFactory.Even(seed: 2));

        second.OutputHash.Should().NotBe(first.OutputHash);

        // Exactly one of these is likely, and the point is that the difference is only in the scoreline: the
        // shape of the match must still be a match.
        second.Events.Should().NotBeEmpty();
        second.TotalMinutesPlayed.Should().BeGreaterThanOrEqualTo(90);
    }

    [Fact]
    public void One_attribute_changes_the_outcome()
    {
        // A snapshot is not just an identity: if the players did not matter, every promise the engine makes
        // about reproducibility would be true and worthless.
        var input = TestMatchFactory.Even();

        var values = input.Home.Squad[9].Attributes.Values.ToArray();
        values[(int)MatchAttributeName.Finishing] += 6;

        var stronger = input with
        {
            Home = input.Home with
            {
                Squad =
                [
                    .. input.Home.Squad.Take(9),
                    input.Home.Squad[9] with { Attributes = PlayerAttributesV1.From(values) },
                    .. input.Home.Squad.Skip(10),
                ],
            },
        };

        var hashes = new HashSet<string>();

        for (var seed = 1UL; seed <= 12; seed++)
        {
            hashes.Add(MatchSimulator.Simulate(stronger with { Seed = seed }).OutputHash);
        }

        var originalHashes = new HashSet<string>();

        for (var seed = 1UL; seed <= 12; seed++)
        {
            originalHashes.Add(MatchSimulator.Simulate(input with { Seed = seed }).OutputHash);
        }

        hashes.Should().NotBeEquivalentTo(originalHashes, "a better finisher changes matches");
    }

    [Fact]
    public void The_golden_hash_for_a_known_snapshot_is_pinned()
    {
        // The most important test in the project. If this fails, an engine change has altered what a historical
        // match would replay as. Engine version 9 re-pinned it (the pass focus gained its shot zones, chance
        // volumes and cross lanes in the rules; the scoreline is still 2-2), and the engine-v8 values were retired
        // with their version. From a
        // version's release onward, any change requires a new engine version and a new labelled constant
        // rather than an updated hash.
        var result = MatchSimulator.Simulate(TestMatchFactory.Even());

        result.OutputHash.Should().Be(GoldenOutputHash);
        result.InputHash.Should().Be(GoldenInputHash);
        result.HomeGoals.Should().Be(2);
        result.AwayGoals.Should().Be(2);
    }

    [Fact]
    public void An_unchanged_configuration_produces_an_unchanged_rules_hash()
    {
        // The rules hash is what a snapshot is frozen against, so it is pinned for the same reason the output
        // hash is: a balance change must be a visible, deliberate act.
        EngineConfiguration.HashOf(EngineRulesV2.Default)
            .Should().Be("3451316f2c8380a2a1fc079fb6318047e65363990f3aef1837dd189973e415b6");
    }

    [Fact]
    public void The_presentation_and_commentary_are_deterministic_too()
    {
        var input = TestMatchFactory.Even();

        var first = MatchSimulator.Simulate(input);
        var second = MatchSimulator.Simulate(input);

        Commentary.CommentaryTokenBuilder.Build(input, first)
            .Should().BeEquivalentTo(Commentary.CommentaryTokenBuilder.Build(input, second), options => options.WithStrictOrdering());

        TestMatchFactory.Play(input).Presentation
            .Should().BeEquivalentTo(
                TestMatchFactory.Play(input).Presentation,
                options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_deliberately_supplied_rules_instance_is_honoured()
    {
        var rules = EngineRulesV2.Default;
        var input = TestMatchFactory.Even();

        var act = () => MatchSimulator.Simulate(input, rules);

        act.Should().NotThrow();
    }

    [Fact]
    public void Rules_that_do_not_match_the_snapshot_are_refused()
    {
        var altered = EngineRulesV2.Default with { BaseFoulBasisPoints = 1_234 };

        var act = () => MatchSimulator.Simulate(TestMatchFactory.Even(), altered);

        act.Should().Throw<InvalidMatchInputException>().WithMessage("*configuration hash*");
    }

    private const string GoldenInputHash =
        "d9c87f42b4e62cf1ab0bceea2a67901eb4bd2ed55a32865a5239bc17de6dbd13";

    private const string GoldenOutputHash =
        "1ad175946bc0e50ad0e448021e3dd32f60d9172d591923299e48626ab1ae5174";
}

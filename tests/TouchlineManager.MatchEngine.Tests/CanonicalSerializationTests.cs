using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Serialization;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The canonical form's guarantees: it does not depend on collection order, it does depend on content, and the
/// three hashes cover what they say they cover.
/// </summary>
public sealed class CanonicalSerializationTests
{
    [Fact]
    public void The_squad_order_does_not_change_the_input_hash()
    {
        var input = TestMatchFactory.Even();

        var shuffled = input with
        {
            Home = input.Home with { Squad = [.. input.Home.Squad.Reverse()] },
            Away = input.Away with { Squad = [.. input.Away.Squad.Reverse()] },
        };

        CanonicalMatchSerializer.InputHash(shuffled)
            .Should().Be(CanonicalMatchSerializer.InputHash(input));
    }

    [Fact]
    public void The_slot_order_does_not_change_the_input_hash()
    {
        var input = TestMatchFactory.Even();

        var shuffled = input with
        {
            Home = input.Home with { Slots = [.. input.Home.Slots.Reverse()] },
        };

        CanonicalMatchSerializer.InputHash(shuffled)
            .Should().Be(CanonicalMatchSerializer.InputHash(input));
    }

    [Fact]
    public void The_content_hash_ignores_the_seed_and_the_input_hash_does_not()
    {
        // The seed is derived from the content hash, so the content hash cannot include it without being
        // circular. Both properties matter: the content hash must be seed-independent, and the input hash — the
        // one stored on the match — must not be.
        var input = TestMatchFactory.Even(seed: 1);
        var reseeded = input with { Seed = 2 };

        CanonicalMatchSerializer.ContentHash(reseeded)
            .Should().Be(CanonicalMatchSerializer.ContentHash(input));

        CanonicalMatchSerializer.InputHash(reseeded)
            .Should().NotBe(CanonicalMatchSerializer.InputHash(input));
    }

    [Fact]
    public void One_attribute_changes_the_input_hash()
    {
        var input = TestMatchFactory.Even();

        var attributes = input.Home.Squad[0].Attributes.Values.ToArray();
        attributes[5] += 1;

        var edited = input with
        {
            Home = input.Home with
            {
                Squad =
                [
                    input.Home.Squad[0] with { Attributes = PlayerAttributesV1.From(attributes) },
                    .. input.Home.Squad.Skip(1),
                ],
            },
        };

        CanonicalMatchSerializer.InputHash(edited)
            .Should().NotBe(CanonicalMatchSerializer.InputHash(input));
    }

    [Fact]
    public void One_name_changes_the_input_hash()
    {
        // Names do not affect the simulation, and they are still part of the snapshot: the hash identifies the
        // whole frozen input, so leaving a field out would let two different snapshots hash alike.
        var input = TestMatchFactory.Even();

        var renamed = input with
        {
            Home = input.Home with
            {
                Squad =
                [
                    input.Home.Squad[0] with { DisplayName = "Somebody Else" },
                    .. input.Home.Squad.Skip(1),
                ],
            },
        };

        CanonicalMatchSerializer.InputHash(renamed)
            .Should().NotBe(CanonicalMatchSerializer.InputHash(input));
    }

    [Fact]
    public void Changing_a_result_changes_the_output_hash()
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even());

        var edited = result with { HomeGoals = result.HomeGoals + 1 };

        CanonicalMatchSerializer.OutputHash(edited)
            .Should().NotBe(CanonicalMatchSerializer.OutputHash(result));
    }

    [Fact]
    public void The_output_hash_covers_the_input_hash_it_was_produced_from()
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even());

        var rebound = result with { InputHash = new string('0', result.InputHash.Length) };

        CanonicalMatchSerializer.OutputHash(rebound)
            .Should().NotBe(CanonicalMatchSerializer.OutputHash(result),
                "a result is bound to the snapshot that produced it");
    }

    [Fact]
    public void The_player_line_order_does_not_change_the_output_hash()
    {
        var result = MatchSimulator.Simulate(TestMatchFactory.Even());

        var shuffled = result with { PlayerLines = [.. result.PlayerLines.Reverse()] };

        CanonicalMatchSerializer.OutputHash(shuffled)
            .Should().Be(CanonicalMatchSerializer.OutputHash(result));
    }

    [Fact]
    public void The_hashes_are_lowercase_hexadecimal()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        CanonicalMatchSerializer.InputHash(input).Should().MatchRegex("^[0-9a-f]{64}$");
        CanonicalMatchSerializer.OutputHash(result).Should().MatchRegex("^[0-9a-f]{64}$");
        result.InputHash.Should().Be(CanonicalMatchSerializer.InputHash(input));
    }

    [Fact]
    public void The_canonical_text_is_labelled_line_by_line()
    {
        // The readable form exists for diagnosing a hash mismatch, so it has to be readable: every value on its
        // own labelled line, and no two adjacent numbers able to be read as one.
        var text = CanonicalMatchSerializer.CanonicalText(TestMatchFactory.Even());

        text.Should().StartWith("match-input-v1\n");
        text.Should().Contain("engineVersion=engine-v1");
        text.Should().Contain("side.home");
        text.Should().Contain("slot.home.1");
    }
}

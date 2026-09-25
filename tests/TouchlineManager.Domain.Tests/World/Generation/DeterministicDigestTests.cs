using FluentAssertions;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Tests.World.Generation;

/// <summary>
/// The digest used for badge seeds and generation provenance (`FIC-7`).
/// </summary>
public sealed class DeterministicDigestTests
{
    [Fact]
    public void A_digest_is_pinned()
    {
        DeterministicDigest
            .Of("stage-3-world", "ENG", "1")
            .Should()
            .Be("715cdc2013b237ea9aa3126ad27cc7d1f0357201163048eec6b92a8dfae8df68");
    }

    [Fact]
    public void The_parts_of_a_digest_are_not_interchangeable()
    {
        // Joined with a separator that cannot occur in the parts, so ("ab", "c") and ("a", "bc") are
        // different inputs rather than one input written two ways.
        DeterministicDigest.Of("ab", "c").Should().NotBe(DeterministicDigest.Of("a", "bc"));
    }

    [Fact]
    public void The_same_parts_always_produce_the_same_digest()
    {
        DeterministicDigest.Of("a", "b", "c").Should().Be(DeterministicDigest.Of("a", "b", "c"));
        DeterministicDigest.Of("a", "b", "c").Should().HaveLength(64);
    }
}

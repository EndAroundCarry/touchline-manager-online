using FluentAssertions;
using TouchlineManager.Domain.Squad.Generation;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Tests.Squad.Generation;

/// <summary>
/// The curated player name pools (`FIC-4`, `FIC-5`, `FIC-8`). A pool is content, so these tests are the
/// review: every entry is invented fiction, and a locale a country can launch in always has one.
/// </summary>
public sealed class PlayerNamePoolsTests
{
    [Fact]
    public void Every_launch_locale_has_a_player_name_pool()
    {
        PlayerNamePools.Keys.Should().BeEquivalentTo(
            ClubNamePools.Keys,
            "a country that can generate clubs must be able to generate their players (FIC-4)");
    }

    [Fact]
    public void No_pool_entry_is_a_blocked_football_identity()
    {
        foreach (var pool in PlayerNamePools.Pools.Values)
        {
            foreach (var name in pool.GivenNames.Concat(pool.Surnames))
            {
                FictionalIdentityBlocklist.ContainsBlockedIdentity(name).Should().BeFalse(
                    $"'{name}' in pool '{pool.Key}' must not be a real football identity (FIC-5)");
            }
        }
    }

    [Fact]
    public void Every_pool_can_name_a_whole_squad_without_repeating_itself()
    {
        foreach (var pool in PlayerNamePools.Pools.Values)
        {
            // The generator pairs given[ordinal % givenCount] with surname[ordinal % surnameCount], so a run
            // of consecutive ordinals is distinct unless the two counts share a small factor. A coprime
            // pair larger than a squad is what makes uniqueness a property rather than a hope (FIC-4).
            var combinations = (long)pool.GivenNames.Count * pool.Surnames.Count;
            var cycle = LeastCommonMultiple(pool.GivenNames.Count, pool.Surnames.Count);

            cycle.Should().BeGreaterThan(
                22,
                $"pool '{pool.Key}' must name a twenty-two-man squad without a repeat");
            combinations.Should().BeGreaterThan(22, $"pool '{pool.Key}' must be larger than a squad");
        }
    }

    [Fact]
    public void A_pool_is_never_empty_and_its_entries_are_unique()
    {
        foreach (var pool in PlayerNamePools.Pools.Values)
        {
            pool.GivenNames.Should().NotBeEmpty();
            pool.Surnames.Should().NotBeEmpty();
            pool.GivenNames.Should().OnlyHaveUniqueItems();
            pool.Surnames.Should().OnlyHaveUniqueItems();
            pool.GivenNames.Should().OnlyContain(name => !string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public void An_unknown_pool_key_is_a_programming_error()
    {
        var act = () => PlayerNamePools.For("atlantis");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_pool_version_is_named_for_the_generation_run()
    {
        PlayerNamePools.Version.Should().Be("player-name-pools-v1", "FIC-8");
    }

    private static int LeastCommonMultiple(int left, int right) =>
        left / GreatestCommonDivisor(left, right) * right;

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }
}

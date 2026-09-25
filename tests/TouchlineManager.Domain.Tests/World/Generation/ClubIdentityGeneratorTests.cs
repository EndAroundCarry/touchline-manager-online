using FluentAssertions;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Tests.World.Generation;

/// <summary>
/// The generated-identity contract: deterministic, unique, and clear of real identities
/// (`FIC-5`, `FIC-7`, `PYR-14`).
/// </summary>
public sealed class ClubIdentityGeneratorTests
{
    private const string Seed = "stage-3-world";

    /// <summary>The launch countries, as (pool key, stable code).</summary>
    private static readonly (string PoolKey, string Code)[] LaunchPools =
    [
        ("england", "ENG"),
        ("spain", "ESP"),
        ("germany", "GER"),
        ("italy", "ITA"),
        ("france", "FRA"),
        ("romania", "ROU"),
    ];

    [Fact]
    public void The_names_for_a_seed_are_pinned()
    {
        // The literal names are the contract. If this fails, either a pool changed without its version
        // being bumped, or the algorithm changed without the generator version being bumped.
        var identities = ClubIdentityGenerator.GenerateDivision(Seed, "england", "ENG", tierNumber: 1, clubCount: 18);

        identities.Take(3).Select(identity => identity.Name).Should().Equal(
            "Stonebury City",
            "Thornwick Athletic",
            "Westbourne Rovers");
    }

    [Fact]
    public void The_badge_seed_for_a_fixed_identity_is_pinned()
    {
        var identities = ClubIdentityGenerator.GenerateDivision(Seed, "england", "ENG", tierNumber: 1, clubCount: 18);

        identities[0].BadgeSeed.Should().Be("2c3cc7d8d0d57862770aee75102c6562");
    }

    [Fact]
    public void The_same_seed_reproduces_the_same_world()
    {
        foreach (var (poolKey, code) in LaunchPools)
        {
            var first = ClubIdentityGenerator.GenerateDivision(Seed, poolKey, code, tierNumber: 1, clubCount: 18);
            var second = ClubIdentityGenerator.GenerateDivision(Seed, poolKey, code, tierNumber: 1, clubCount: 18);

            second.Should().Equal(first, "PYR-14 requires the same seed and version to reproduce the same tier");
        }
    }

    [Fact]
    public void A_different_seed_produces_a_different_world()
    {
        foreach (var (poolKey, code) in LaunchPools)
        {
            var baseline = ClubIdentityGenerator.GenerateDivision(Seed, poolKey, code, tierNumber: 1, clubCount: 18);
            var other = ClubIdentityGenerator.GenerateDivision("another-world", poolKey, code, tierNumber: 1, clubCount: 18);

            // Two seeds could in principle pick the same name offset, so the guarantee that a different
            // seed is a different world rests on the badge seed, which is a digest of the seed itself.
            // The names are asserted to differ in sequence, which is what makes the offset worth having.
            other.Select(identity => identity.Name)
                .Should()
                .NotEqual(baseline.Select(identity => identity.Name));

            other.Select(identity => identity.BadgeSeed)
                .Should()
                .NotIntersectWith(baseline.Select(identity => identity.BadgeSeed));
        }
    }

    [Fact]
    public void One_division_names_every_club_differently()
    {
        foreach (var (poolKey, code) in LaunchPools)
        {
            var identities = ClubIdentityGenerator.GenerateDivision(Seed, poolKey, code, tierNumber: 1, clubCount: 18);

            identities.Select(identity => identity.Name).Should().OnlyHaveUniqueItems();
            identities.Select(identity => identity.BadgeSeed).Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void Ordinals_do_not_restart_per_tier_so_generated_tiers_never_collide()
    {
        // The bug this guards: if each tier restarted at ordinal zero, every provisioned tier would
        // propose the same 18 names and the unique name index would reject the second tier (PYR-11).
        var names = new List<string>();

        for (var tier = 1; tier <= 12; tier++)
        {
            names.AddRange(
                ClubIdentityGenerator
                    .GenerateDivision(Seed, "england", "ENG", tier, clubCount: 18)
                    .Select(identity => identity.Name));
        }

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_very_deep_pyramid_is_still_named_uniquely_without_a_ceiling()
    {
        // PYR-11 says there is no maximum tier. Past the pool's combination cycle the generator falls
        // back to qualifiers and numerals rather than repeating a name or throwing.
        var names = new List<string>();

        for (var tier = 1; tier <= 40; tier++)
        {
            names.AddRange(
                ClubIdentityGenerator
                    .GenerateDivision(Seed, "england", "ENG", tier, clubCount: 18)
                    .Select(identity => identity.Name));
        }

        names.Should().HaveCount(720).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_club_name_combines_a_place_from_its_pool_with_a_suffix_from_its_pool()
    {
        var pool = ClubNamePools.For("italy");
        var identities = ClubIdentityGenerator.GenerateDivision(Seed, "italy", "ITA", tierNumber: 3, clubCount: 18);

        foreach (var identity in identities)
        {
            pool.Places.Select(place => place.Name).Should().Contain(identity.City);
            pool.ClubSuffixes.Should().Contain(suffix => identity.Name.EndsWith(suffix, StringComparison.Ordinal));
            identity.Region.Should().Be(pool.Places.Single(place => place.Name == identity.City).Region);
        }
    }

    [Fact]
    public void Short_names_are_folded_and_four_characters()
    {
        // A three-letter column that renders "PEÑ" on one device and "PEN" on another is a bug report
        // waiting to happen, so the short name is deliberately accent-free.
        foreach (var (poolKey, code) in LaunchPools)
        {
            foreach (var identity in ClubIdentityGenerator.GenerateDivision(Seed, poolKey, code, tierNumber: 1, clubCount: 18))
            {
                identity.ShortName.Should().HaveLength(4);
                identity.ShortName.Should().MatchRegex("^[A-Z0-9]{4}$");
            }
        }
    }

    [Fact]
    public void Two_countries_never_propose_the_same_club_name()
    {
        // Names are unique per world, so a collision between two countries would be a world-seeding
        // failure rather than a data quality issue.
        var names = LaunchPools
            .SelectMany(country => ClubIdentityGenerator
                .GenerateDivision(Seed, country.PoolKey, country.Code, tierNumber: 1, clubCount: 18))
            .Select(identity => Club.NormalizeName(identity.Name));

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void No_generated_identity_resembles_a_real_football_identity()
    {
        // FIC-5: the check that has to run before a pool ships.
        foreach (var (poolKey, code) in LaunchPools)
        {
            foreach (var tier in new[] { 1, 2, 5, 12 })
            {
                foreach (var identity in ClubIdentityGenerator.GenerateDivision(Seed, poolKey, code, tier, clubCount: 18))
                {
                    FictionalIdentityBlocklist.ContainsBlockedIdentity(identity.Name).Should().BeFalse(
                        $"'{identity.Name}' must not reproduce a real club or competition identity");
                }
            }
        }
    }

    [Fact]
    public void One_tier_holds_exactly_the_combinations_its_cycle_reaches()
    {
        // The uniqueness argument in one assertion: combinations are injective over a cycle, so the
        // generator needs no collision retry loop and cannot loop forever on a small pool.
        foreach (var key in ClubNamePools.Keys)
        {
            var pool = ClubNamePools.For(key);
            var ticks = new HashSet<(string Place, string Suffix, int Cycle)>();

            for (var ordinal = 0; ordinal < pool.CombinationsPerCycle; ordinal++)
            {
                var identity = ClubIdentityGenerator.Generate(Seed, pool, "XXX", ordinal);
                var suffix = pool.ClubSuffixes.Single(candidate =>
                    identity.Name.EndsWith(candidate, StringComparison.Ordinal));

                ticks.Add((identity.City, suffix, 0)).Should().BeTrue(
                    "ordinal {0} produced a duplicate combination in '{1}'",
                    ordinal,
                    key);
            }

            ticks.Should().HaveCount(pool.CombinationsPerCycle);
        }
    }

    [Fact]
    public void A_pool_that_does_not_exist_is_a_programming_error()
    {
        var act = () => ClubNamePools.For("atlantis");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_negative_ordinal_is_a_programming_error()
    {
        var pool = ClubNamePools.For("england");

        var act = () => ClubIdentityGenerator.Generate(Seed, pool, "ENG", -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Every_pool_can_name_a_full_tier_many_times_over()
    {
        foreach (var key in ClubNamePools.Keys)
        {
            ClubNamePools.For(key).NamedCapacity.Should().BeGreaterThan(
                18 * 10,
                "'{0}' must not run out of names within a realistic pyramid depth",
                key);
        }
    }
}

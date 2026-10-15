using FluentAssertions;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// The crest catalogue the server accepts: a shape, and the stripes laid on it, chosen independently. The web
/// client draws these codes, so both lists are pinned in full: a code added on one side only would let a
/// manager pick a crest that cannot be saved, or save one that cannot be drawn. The same lists are pinned in
/// the web client's <c>crest-designs.spec.ts</c>.
/// </summary>
public sealed class CrestDesignsTests
{
    private static readonly string[] ExpectedShapes =
    [
        "shield",
        "round",
        "pointed",
        "rounded",
        "oval",
        "hexagon",
        "diamond",
        "octagon",
        "square",
        "arch",
        "pentagon",
        "teardrop",
        "flat-base",
        "pennant",
        "gothic",
        "waisted",
        "chamfered",
        "triangle",
        "scooped",
        "wide-oval",
    ];

    private static readonly string[] ExpectedPatterns =
    [
        "stripes",
        "hoops",
        "halves",
        "fess",
        "quarters",
        "sash",
        "diagonals",
        "diagonal",
        "cross",
        "saltire",
        "chevron",
        "pale",
        "band",
        "checks",
        "star",
        "plain",
    ];

    [Fact]
    public void The_shapes_are_the_twenty_the_web_client_draws_in_the_same_order()
    {
        CrestDesigns.Shapes.Should().Equal(ExpectedShapes);
    }

    [Fact]
    public void The_stripes_are_the_ones_the_web_client_draws_in_the_same_order()
    {
        CrestDesigns.Patterns.Should().Equal(ExpectedPatterns);
    }

    [Fact]
    public void Every_code_fits_the_column_it_is_stored_in()
    {
        CrestDesigns.Shapes.Concat(CrestDesigns.Patterns).Should().OnlyContain(code => code.Length <= CrestDesigns.MaxLength);
    }

    [Theory]
    [InlineData("shield", true)]
    [InlineData("wide-oval", true)]
    [InlineData("Shield", false)]
    [InlineData("stripes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_code_in_the_shape_list_is_a_known_shape(string? code, bool known)
    {
        CrestDesigns.IsKnownShape(code).Should().Be(known);
    }

    [Theory]
    [InlineData("stripes", true)]
    [InlineData("plain", true)]
    [InlineData("Stripes", false)]
    [InlineData("shield", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_code_in_the_stripes_list_is_a_known_pattern(string? code, bool known)
    {
        CrestDesigns.IsKnownPattern(code).Should().Be(known);
    }

    [Fact]
    public void A_generated_shape_and_stripes_are_stable_for_a_seed_and_always_in_their_lists()
    {
        CrestDesigns.GeneratedShape("0123456789abcdef").Should().Be(CrestDesigns.GeneratedShape("0123456789abcdef"));
        CrestDesigns.GeneratedPattern("0123456789abcdef").Should().Be(CrestDesigns.GeneratedPattern("0123456789abcdef"));

        for (var i = 0; i < 200; i++)
        {
            CrestDesigns.Shapes.Should().Contain(CrestDesigns.GeneratedShape($"seed-{i}"));
            CrestDesigns.Patterns.Should().Contain(CrestDesigns.GeneratedPattern($"seed-{i}"));
        }
    }

    [Fact]
    public void Generated_crests_spread_across_the_catalogue_rather_than_piling_on_one()
    {
        var seeds = Enumerable.Range(0, 400).Select(i => $"club-seed-{i}").ToList();

        seeds.Select(CrestDesigns.GeneratedShape).Distinct().Count().Should().BeGreaterThanOrEqualTo(15);
        seeds.Select(CrestDesigns.GeneratedPattern).Distinct().Count().Should().BeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void The_generated_shape_and_stripes_vary_independently_of_one_another()
    {
        var pairs = Enumerable.Range(0, 400)
            .Select(i => (Shape: CrestDesigns.GeneratedShape($"club-seed-{i}"), Pattern: CrestDesigns.GeneratedPattern($"club-seed-{i}")))
            .Distinct()
            .Count();

        pairs.Should().BeGreaterThan(180, "a shape does not decide its stripes");
    }

    [Fact]
    public void A_chosen_shape_and_stripes_win_over_the_generated_ones_and_an_unknown_choice_falls_back()
    {
        CrestDesigns.ResolveShape("seed", "round").Should().Be("round");
        CrestDesigns.ResolveShape("seed", null).Should().Be(CrestDesigns.GeneratedShape("seed"));
        CrestDesigns.ResolveShape("seed", "dragon").Should().Be(CrestDesigns.GeneratedShape("seed"));

        CrestDesigns.ResolvePattern("seed", "star").Should().Be("star");
        CrestDesigns.ResolvePattern("seed", null).Should().Be(CrestDesigns.GeneratedPattern("seed"));
        CrestDesigns.ResolvePattern("seed", "polka-dots").Should().Be(CrestDesigns.GeneratedPattern("seed"));
    }
}

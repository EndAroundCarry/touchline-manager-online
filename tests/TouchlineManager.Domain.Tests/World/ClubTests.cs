using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// Club identity: what uniqueness is enforced on, and what a tier decides.
/// </summary>
public sealed class ClubTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Northbridge United", "northbridge-united")]
    [InlineData("  Real Aldervale  ", "real-aldervale")]
    [InlineData("Atlético Rioja", "atletico-rioja")]
    [InlineData("FC St. Aldwyn", "fc-st-aldwyn")]
    [InlineData("Club 24/7", "club-24-7")]
    public void A_slug_is_lowercase_folded_and_hyphenated(string name, string expected)
    {
        // Accents are folded rather than dropped, so "Atlético" and "Atletico" cannot become two
        // different slugs for the same generated name.
        Club.SlugFrom(name).Should().Be(expected);
    }

    [Fact]
    public void A_normalized_name_is_what_uniqueness_is_enforced_on()
    {
        Club.NormalizeName("  Northbridge United ").Should().Be("NORTHBRIDGE UNITED");
    }

    [Fact]
    public void The_slug_never_starts_or_ends_with_a_separator()
    {
        Club.SlugFrom("-- Vale --").Should().Be("vale");
        Club.SlugFrom("À.B.C").Should().Be("a-b-c");
    }

    [Fact]
    public void A_generated_club_is_active_and_takes_its_scale_from_its_tier()
    {
        var club = Generate(tier: 1);

        club.Status.Should().Be(ClubStatus.Active);
        club.StadiumBaseline.Should().Be(WorldRuleSet.OpeningStadiumBaselineForTier(1));
        club.Reputation.Should().Be(WorldRuleSet.OpeningReputationForTier(1));
        club.Version.Should().Be(1);
    }

    [Fact]
    public void A_deeper_tier_club_is_less_wealthy_and_less_reputed_than_a_top_tier_one()
    {
        var top = Generate(tier: 1);
        var deep = Generate(tier: 4);

        deep.StadiumBaseline.Should().BeLessThan(top.StadiumBaseline);
        deep.Reputation.Should().BeLessThan(top.Reputation);
    }

    [Fact]
    public void The_name_and_its_derived_forms_cannot_disagree()
    {
        var club = Club.Generate(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new ClubIdentity("  Aston Vale  ", "AVL", "Vale", "Midlands", "seed-1"),
            tier: 1,
            foundingGameYear: 2026,
            now: Now);

        club.Name.Should().Be("Aston Vale");
        club.NormalizedName.Should().Be("ASTON VALE");
        club.Slug.Should().Be("aston-vale");
    }

    [Fact]
    public void Generating_a_club_without_a_name_is_a_programming_error()
    {
        var act = () => Club.Generate(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new ClubIdentity("  ", "AVL", "Vale", "Midlands", "seed-1"),
            tier: 1,
            foundingGameYear: 2026,
            now: Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_status_round_trips_through_its_code()
    {
        ClubStatus.Active.ToCode().Should().Be("active");
        ClubStatuses.FromCode("retired").Should().Be(ClubStatus.Retired);

        var act = () => ClubStatuses.FromCode("dissolved");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Club Generate(int tier) =>
        Club.Generate(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new ClubIdentity("Northbridge United", "NOR", "Northbridge", "Ridings", "seed-1"),
            tier,
            foundingGameYear: 2026,
            now: Now);
}

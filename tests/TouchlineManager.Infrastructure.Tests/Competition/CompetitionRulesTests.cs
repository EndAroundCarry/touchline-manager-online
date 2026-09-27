using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// The competition-rules read against real PostgreSQL: a division's stored draw and every club's key
/// (`TBL-10`, `TBL-11`).
/// </summary>
/// <remarks>
/// Nothing here depends on a round having been played — the rules and the draw are written when the world is
/// seeded — so the read is tested in a division no other test plays in, and its own assertions are about what
/// the database holds rather than about a projection a workflow produced.
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class CompetitionRulesTests
{
    /// <summary>Initializes the tests.</summary>
    public CompetitionRulesTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests read.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task The_rules_read_the_seasons_stored_draw_and_a_key_for_every_club()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var stored = await (
            from divisionSeason in db.DivisionSeasons
            join division in db.Divisions on divisionSeason.DivisionId equals division.Id
            join country in db.Countries on division.CountryId equals country.Id
            where country.Code == LaunchCountries.ItalyCode
            select new
            {
                divisionSeason.DivisionId,
                divisionSeason.TieDrawSeed,
                divisionSeason.TieDrawHash,
            })
            .SingleAsync();

        var rules = await scope.ServiceProvider.GetRequiredService<ICompetitionQueries>()
            .GetDivisionRulesAsync(stored.DivisionId, CancellationToken.None);

        rules.Should().NotBeNull();

        rules!.TieDrawSeed.Should().Be(stored.TieDrawSeed, "TBL-11: the page shows the season's stored draw");
        rules.TieDrawHash.Should().Be(stored.TieDrawHash);
        rules.TieDrawHash.Should().Be(
            DeterministicDigest.Of(rules.TieDrawSeed),
            "TBL-11: the published hash is the digest of the stored seed, so the draw can be checked");

        rules.Clubs.Should().HaveCount(WorldRuleSet.ClubsPerDivision, "WORLD-4");
        rules.Clubs.Select(club => club.ClubId).Should().OnlyHaveUniqueItems();
        rules.Clubs.Select(club => club.DrawKey).Should().OnlyHaveUniqueItems(
            "the draw separates every club, so a collision would make the last tie-breaker useless");

        foreach (var club in rules.Clubs)
        {
            club.DrawKey.Should().Be(
                StandingsCalculator.DrawKeyOf(rules.TieDrawSeed, club.ClubId),
                "the key is derived from the stored seed rather than stored beside it");
        }
    }

    [Fact]
    public async Task An_unknown_division_has_no_rules()
    {
        await using var scope = Fixture.CreateScope();

        var rules = await scope.ServiceProvider.GetRequiredService<ICompetitionQueries>()
            .GetDivisionRulesAsync(Guid.CreateVersion7(), CancellationToken.None);

        rules.Should().BeNull();
    }
}

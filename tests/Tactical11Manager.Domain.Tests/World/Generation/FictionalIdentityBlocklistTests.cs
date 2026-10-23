using FluentAssertions;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Tests.World.Generation;

/// <summary>
/// The fictional-data backstop (`FIC-1`, `FIC-5`).
/// </summary>
public sealed class FictionalIdentityBlocklistTests
{
    [Fact]
    public void Every_pool_place_is_clear_of_a_real_identity()
    {
        foreach (var key in ClubNamePools.Keys)
        {
            foreach (var place in ClubNamePools.For(key).Places)
            {
                FictionalIdentityBlocklist.ContainsBlockedIdentity(place.Name).Should().BeFalse(
                    "'{0}' in pool '{1}' resembles a real identity",
                    place.Name,
                    key);

                FictionalIdentityBlocklist.ContainsBlockedIdentity(place.Region).Should().BeFalse(
                    "region '{0}' in pool '{1}' resembles a real identity",
                    place.Region,
                    key);
            }
        }
    }

    [Fact]
    public void A_real_club_name_is_caught()
    {
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Manchester United").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("real madrid").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Bayern Sport").Should().BeTrue();
    }

    [Fact]
    public void The_real_clubs_and_players_of_belgium_portugal_and_the_netherlands_are_caught()
    {
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Benfica Sport").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("FC Porto").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Ajax Vooruit").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Club Brugge").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Racing Genk").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Kevin De Bruyne").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Eden Hazard").Should().BeTrue();
    }

    [Fact]
    public void A_player_surname_is_caught()
    {
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Ashvale Ronaldo").Should().BeTrue();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Corvalán Mbappé").Should().BeTrue();
    }

    [Fact]
    public void An_invented_name_that_merely_contains_a_blocked_fragment_is_allowed()
    {
        // Matching is by whole word. A substring rule would reject "Barceloneta Laporte" — an invented
        // place — which is exactly the kind of false positive that gets a guardrail switched off.
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Barceloneta Union").Should().BeFalse();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Milano Sud").Should().BeFalse();
        FictionalIdentityBlocklist.ContainsBlockedIdentity("Parisien Centre").Should().BeFalse();
    }

    [Fact]
    public void The_blocklist_is_not_empty()
    {
        FictionalIdentityBlocklist.Entries.Should().NotBeEmpty();
    }
}

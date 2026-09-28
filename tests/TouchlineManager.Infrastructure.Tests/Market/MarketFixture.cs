using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Market;

/// <summary>
/// A seeded world of its own for the market tests.
/// </summary>
/// <remarks>
/// Its own database, deliberately: an auction resolution moves a player from one club to another, so a suite
/// that shares the world collection would leave a club with twenty-one players and break the seeding
/// assertions that check every club's squad. Every market test then runs against a world it alone mutates.
/// </remarks>
public sealed class MarketFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public MarketFixture()
        : base("touchline_market")
    {
    }
}

/// <summary>Shares one seeded world across the market test classes.</summary>
[CollectionDefinition(Name)]
public sealed class MarketCollection : ICollectionFixture<MarketFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "market";
}

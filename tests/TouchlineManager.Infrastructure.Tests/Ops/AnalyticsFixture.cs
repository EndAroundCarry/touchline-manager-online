using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Ops;

/// <summary>
/// A seeded world of its own for the operational funnel tests.
/// </summary>
/// <remarks>
/// Its own database, deliberately: the funnel tests claim clubs and close tenures, and the shared world
/// collection's seeding assertions read an untouched pyramid. Running them against a world they alone
/// mutate keeps both honest — and lets the counts be asserted against a known baseline rather than as
/// deltas against whatever another suite left behind.
/// </remarks>
public sealed class AnalyticsFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public AnalyticsFixture()
        : base("touchline_analytics")
    {
    }
}

/// <summary>Shares one seeded world across the operational funnel test classes.</summary>
[CollectionDefinition(Name)]
public sealed class AnalyticsCollection : ICollectionFixture<AnalyticsFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "analytics";
}

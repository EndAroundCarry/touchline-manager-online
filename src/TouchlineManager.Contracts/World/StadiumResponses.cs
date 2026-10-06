namespace TouchlineManager.Contracts.World;

/// <summary>One kind of place in a club's stadium, with what it sells for and costs to add (`STAD-3`, `STAD-4`).</summary>
/// <param name="Stand">The stand, as a stable lowercase code: <c>standing</c>, <c>seating</c>, <c>covered_seating</c> or <c>vip</c>.</param>
/// <param name="Seats">How many places of this kind the ground holds.</param>
/// <param name="TicketPriceMinor">What one ticket costs, in minor units, at the club's tier.</param>
/// <param name="BuildCostMinor">What one more place of this kind costs to build, in minor units.</param>
/// <param name="ExpectedSold">
/// How many of these places a home match sells at mid-table form: the lesser of the places and the crowd that
/// wants them (`STAD-5`). When it is below <paramref name="Seats"/>, more of this kind would sit empty.
/// </param>
public sealed record StadiumStandResponse(
    string Stand,
    int Seats,
    long TicketPriceMinor,
    long BuildCostMinor,
    int ExpectedSold);

/// <summary>A club's stadium: its size, its places by kind, and what it takes at the gate (`STAD-1`…`STAD-6`).</summary>
/// <param name="ClubId">The club that owns the ground.</param>
/// <param name="Level">The ground's level, 1–<paramref name="MaxLevel"/>: how many 5,000-place blocks it spans.</param>
/// <param name="MaxLevel">The highest level a ground reaches.</param>
/// <param name="Capacity">Every place in the ground.</param>
/// <param name="MaxCapacity">The most places a ground can hold.</param>
/// <param name="SeatsPerLevel">How many places a level spans.</param>
/// <param name="SeatsToNextLevel">
/// How many more places move the ground up a level, which is when its picture changes. Zero at the top level.
/// </param>
/// <param name="PrimaryColour">The club's colour, which the seats are drawn in.</param>
/// <param name="SecondaryColour">The club's second colour, for the trim.</param>
/// <param name="Stands">The four kinds of place, in the order a manager reads them.</param>
/// <param name="ExpectedDemand">The people who would come to a mid-table home match, before the ground limits them.</param>
/// <param name="FullHouseMinor">What a sell-out would take at the gate, in minor units.</param>
/// <param name="ExpectedGateMinor">What a mid-table home match takes at the gate, in minor units.</param>
/// <param name="AvailableMinor">What the club can spend on building, after reservations (`FIN-10`).</param>
/// <param name="Version">The ground's version, which is the strong entity tag a build order is made against.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record StadiumResponse(
    Guid ClubId,
    int Level,
    int MaxLevel,
    int Capacity,
    int MaxCapacity,
    int SeatsPerLevel,
    int SeatsToNextLevel,
    string PrimaryColour,
    string SecondaryColour,
    IReadOnlyList<StadiumStandResponse> Stands,
    int ExpectedDemand,
    long FullHouseMinor,
    long ExpectedGateMinor,
    long AvailableMinor,
    long Version,
    DateTimeOffset ServerTime);

/// <summary>A manager's order to add places to the stadium (`STAD-4`).</summary>
/// <param name="Stand">The kind of place to build, as a stable lowercase code.</param>
/// <param name="Count">How many places to add.</param>
public sealed record BuildSeatsRequest(string? Stand, int Count);

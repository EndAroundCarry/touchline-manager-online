namespace Tactical11Manager.Domain.Fans;

/// <summary>
/// What one club's fans expected from one fixture, frozen when the team sheets lock (`FAN-4`, `FAN-10`).
/// </summary>
/// <remarks>
/// The result is judged against this and not against what the fans would think after the match, so a squad that changes between the
/// lock and the publication cannot change how the fans react. Each fixture has two, one for each side.
/// </remarks>
public sealed class FixtureFanExpectation
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private FixtureFanExpectation()
    {
    }

    /// <summary>Gets the fixture.</summary>
    public Guid FixtureId { get; private set; }

    /// <summary>Gets the club whose fans held the expectation.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the points the fans expected, in hundredths.</summary>
    public int ExpectedPointsCentis { get; private set; }

    /// <summary>Gets the chance of a win they gave, in basis points.</summary>
    public int WinBp { get; private set; }

    /// <summary>Gets the chance of a draw they gave, in basis points.</summary>
    public int DrawBp { get; private set; }

    /// <summary>Gets the chance of a defeat they gave, in basis points.</summary>
    public int LossBp { get; private set; }

    /// <summary>Gets the label their expectation fell under.</summary>
    public FanExpectationLabel Label { get; private set; }

    /// <summary>Gets the goals the club scores in the score they would bet on.</summary>
    public int PredictedGoalsFor { get; private set; }

    /// <summary>Gets the goals the club concedes in the score they would bet on.</summary>
    public int PredictedGoalsAgainst { get; private set; }

    /// <summary>Gets when the expectation was frozen.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The expectation as the rules read it.</summary>
    public FanExpectation Expectation =>
        new(ExpectedPointsCentis, WinBp, DrawBp, LossBp, Label, PredictedGoalsFor, PredictedGoalsAgainst);

    /// <summary>Freezes an expectation.</summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="clubId">The club whose fans held it.</param>
    /// <param name="expectation">What they expected.</param>
    /// <param name="now">The current instant.</param>
    public static FixtureFanExpectation Freeze(Guid fixtureId, Guid clubId, FanExpectation expectation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(expectation);

        return new FixtureFanExpectation
        {
            FixtureId = fixtureId,
            ClubId = clubId,
            ExpectedPointsCentis = expectation.ExpectedPointsCentis,
            WinBp = expectation.WinBp,
            DrawBp = expectation.DrawBp,
            LossBp = expectation.LossBp,
            Label = expectation.Label,
            PredictedGoalsFor = expectation.PredictedGoalsFor,
            PredictedGoalsAgainst = expectation.PredictedGoalsAgainst,
            CreatedAt = now,
        };
    }
}

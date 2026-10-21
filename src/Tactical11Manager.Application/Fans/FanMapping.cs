using Tactical11Manager.Contracts.Fans;
using Tactical11Manager.Domain.Fans;

namespace Tactical11Manager.Application.Fans;

/// <summary>
/// Maps the fans to their transport shapes (`FAN-4`, `FAN-10`).
/// </summary>
public static class FanMapping
{
    /// <summary>Projects what the fans expect from a match.</summary>
    /// <param name="expectation">The expectation.</param>
    public static FanExpectationResponse ToResponse(this FanExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(expectation);

        return new FanExpectationResponse(
            expectation.ExpectedPointsCentis,
            expectation.WinBp,
            expectation.DrawBp,
            expectation.LossBp,
            FanExpectationLabels.Code(expectation.Label),
            FanExpectationLabels.Phrase(expectation.Label),
            expectation.PredictedGoalsFor,
            expectation.PredictedGoalsAgainst);
    }
}

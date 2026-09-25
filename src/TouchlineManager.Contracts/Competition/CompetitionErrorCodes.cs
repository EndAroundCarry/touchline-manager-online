namespace TouchlineManager.Contracts.Competition;

/// <summary>
/// The stable refusal codes the competition and team-sheet surfaces return.
/// </summary>
/// <remarks>
/// A client branches on these rather than on prose. They are separate from <c>SquadErrorCodes</c> and
/// <c>WorldErrorCodes</c> because they answer different questions: that pair refuses a squad read or a
/// takeover, this one refuses a fixture that is not yours, a fixture whose deadline has passed, and a
/// selection that breaks a team-sheet rule.
/// </remarks>
public static class CompetitionErrorCodes
{
    /// <summary>No fixture exists with the requested identity.</summary>
    public const string FixtureNotFound = "FIXTURE_NOT_FOUND";

    /// <summary>No division exists with the requested identity.</summary>
    public const string DivisionNotFound = "DIVISION_NOT_FOUND";

    /// <summary>The caller holds a club, but not one of the two playing the fixture.</summary>
    public const string FixtureNotYours = "FIXTURE_NOT_YOURS";

    /// <summary>The fixture has locked, so its team sheet can no longer be changed (`CAL-3`, `SQ-7`).</summary>
    public const string FixtureLocked = "FIXTURE_LOCKED";

    /// <summary>The club has no default tactical plan, so there is nothing to prepare a side from (`INS-11`).</summary>
    public const string NoDefaultPlan = "NO_DEFAULT_PLAN";

    /// <summary>The submitted selection breaks a team-sheet rule; the response carries the issues (`SQ-4`).</summary>
    public const string TeamSheetValidationFailed = "TEAM_SHEET_VALIDATION_FAILED";
}

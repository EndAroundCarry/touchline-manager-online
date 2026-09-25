using TouchlineManager.Api.Http;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.Competition;

/// <summary>
/// Turns a competition read or write refusal into a status and a stable code.
/// </summary>
/// <remarks>
/// <para>
/// One mapper for the fixture reads and the team-sheet read and save, because they refuse for the same
/// reasons and a client branches on the same vocabulary. The "not yours" answer is parameterized because
/// the two surfaces naming it differently is deliberate: a manager reading a squad they do not hold gets
/// <c>CLUB_NOT_MANAGED</c>, and one preparing a side for a fixture they do not play in gets
/// <c>FIXTURE_NOT_YOURS</c>, which tells them the fixture rather than the club was the problem.
/// </para>
/// <para>
/// The authorization refusals are distinct for the same reason the squad reads keep them distinct: a
/// client that receives <c>NO_CLUB</c> knows to send the manager to onboarding, and one that receives a
/// stale-view answer knows to refresh, and collapsing them into one forbidden response would leave it
/// guessing (master plan §10.9, §15.4).
/// </para>
/// </remarks>
internal static class CompetitionRefusals
{
    /// <summary>Maps a read refusal.</summary>
    /// <param name="outcome">The refusal.</param>
    /// <param name="notYoursCode">The code for "you do not hold a club in this".</param>
    /// <param name="notYoursDetail">What to tell the manager in that case.</param>
    public static IResult Read(
        CompetitionReadOutcome outcome,
        string notYoursCode,
        string notYoursDetail) => outcome switch
        {
            CompetitionReadOutcome.NoClub => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                SquadErrorCodes.NoClub,
                "No club.",
                "You do not manage a club yet, so there is nothing to read."),

            CompetitionReadOutcome.NoManagerProfile => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                WorldErrorCodes.ManagerProfileRequired,
                "No manager profile.",
                "Create your manager profile before managing a club."),

            CompetitionReadOutcome.ClubNotFound => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.ClubNotFound,
                "No such club.",
                "That club does not exist in this world."),

            CompetitionReadOutcome.WorldNotSeeded => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                WorldErrorCodes.WorldNotSeeded,
                "No world yet.",
                "The world has not been created."),

            CompetitionReadOutcome.DivisionNotFound => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                CompetitionErrorCodes.DivisionNotFound,
                "No such division.",
                "That division does not exist in this world, or it has no season in progress."),

            CompetitionReadOutcome.ClubNotManaged => ProblemResults.Code(
                StatusCodes.Status403Forbidden,
                notYoursCode,
                "Not yours.",
                notYoursDetail),

            _ => ProblemResults.Code(
                StatusCodes.Status404NotFound,
                CompetitionErrorCodes.FixtureNotFound,
                "No such fixture.",
                "That fixture does not exist in this world."),
        };

    /// <summary>Maps a team-sheet save refusal.</summary>
    /// <param name="result">The save result.</param>
    public static IResult Write(SaveFixtureTeamSheetResult result) => result.Outcome switch
    {
        SaveFixtureTeamSheetOutcome.Invalid => ProblemResults.Code(
            StatusCodes.Status400BadRequest,
            CompetitionErrorCodes.TeamSheetValidationFailed,
            "That side is not valid.",
            "Fix the highlighted slots and save again.",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["validation"] = result.Validation,
            }),

        SaveFixtureTeamSheetOutcome.FixtureLocked => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            CompetitionErrorCodes.FixtureLocked,
            "The team sheet has locked.",
            "Team sheets lock thirty minutes before kickoff. This selection can no longer be changed."),

        SaveFixtureTeamSheetOutcome.NoDefaultPlan => ProblemResults.Code(
            StatusCodes.Status409Conflict,
            CompetitionErrorCodes.NoDefaultPlan,
            "No tactic to prepare from.",
            "Save a tactical plan first; a fixture's side is prepared from the club's default plan."),

        SaveFixtureTeamSheetOutcome.FixtureNotFound => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            CompetitionErrorCodes.FixtureNotFound,
            "No such fixture.",
            "That fixture does not exist in this world."),

        SaveFixtureTeamSheetOutcome.FixtureNotYours => NotYours(),

        SaveFixtureTeamSheetOutcome.PreconditionRequired => ProblemResults.Code(
            StatusCodes.Status428PreconditionRequired,
            ApiErrorCodes.PreconditionRequired,
            "A version is required.",
            "Send the team sheet's current entity tag in If-Match so a concurrent change is not overwritten."),

        SaveFixtureTeamSheetOutcome.PreconditionFailed => ProblemResults.Code(
            StatusCodes.Status412PreconditionFailed,
            ApiErrorCodes.PreconditionFailed,
            "The team sheet changed.",
            "Reload the selection and reapply the change."),

        SaveFixtureTeamSheetOutcome.NoClub => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            SquadErrorCodes.NoClub,
            "No club.",
            "You do not manage a club yet, so there is no side to prepare."),

        SaveFixtureTeamSheetOutcome.NoManagerProfile => ProblemResults.Code(
            StatusCodes.Status403Forbidden,
            WorldErrorCodes.ManagerProfileRequired,
            "No manager profile.",
            "Create your manager profile before managing a club."),

        SaveFixtureTeamSheetOutcome.WorldNotSeeded => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.WorldNotSeeded,
            "No world yet.",
            "The world has not been created."),

        _ => ProblemResults.Code(
            StatusCodes.Status404NotFound,
            WorldErrorCodes.ClubNotFound,
            "No such club.",
            "That club does not exist in this world."),
    };

    /// <summary>The answer for a manager looking at a fixture their club does not play in.</summary>
    public static IResult NotYours() => ProblemResults.Code(
        StatusCodes.Status403Forbidden,
        CompetitionErrorCodes.FixtureNotYours,
        "Not your fixture.",
        "Your club does not play in that fixture.");
}

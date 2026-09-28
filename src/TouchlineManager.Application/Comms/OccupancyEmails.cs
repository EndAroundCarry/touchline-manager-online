using TouchlineManager.Application.Abstractions.Auth;

namespace TouchlineManager.Application.Comms;

/// <summary>
/// The notification emails the occupancy ladder and the reminder job send (`OCC-1`, `COM-4`).
/// </summary>
/// <remarks>
/// Plain text plus a minimal HTML alternative, like the auth emails, and never localized in the database. The
/// text is composed here rather than at the call site so the inbox message and the email about the same event
/// stay in step.
/// </remarks>
public static class OccupancyEmails
{
    /// <summary>Builds the warning a lapse in activity earns (`OCC-1`).</summary>
    /// <param name="to">The manager's address.</param>
    /// <param name="clubName">The club they hold.</param>
    /// <param name="daysInactive">How many whole days they have been away.</param>
    public static EmailMessage InactivityWarning(string to, string clubName, int daysInactive) => new(
        to,
        $"We have not seen you in a while — {clubName}",
        $"""
         You have been away from Touchline Manager for {daysInactive} days and are still the manager of
         {clubName}.

         Log in to keep full control of your club. If you remain away, the AI will take over routine
         decisions, and eventually your tenure will end and the club will return to the AI.
         """,
        $"""
         <p>You have been away from Touchline Manager for {daysInactive} days and are still the manager of
         <strong>{clubName}</strong>.</p>
         <p>Log in to keep full control of your club. If you remain away, the AI will take over routine
         decisions, and eventually your tenure will end and the club will return to the AI.</p>
         """);

    /// <summary>Builds the notice that a tenure closed for inactivity (`OCC-3`).</summary>
    /// <param name="to">The manager's address.</param>
    /// <param name="clubName">The club they held.</param>
    /// <param name="daysInactive">How many whole days they were away.</param>
    public static EmailMessage InactivityClosed(string to, string clubName, int daysInactive) => new(
        to,
        $"Your tenure at {clubName} has ended",
        $"""
         After {daysInactive} days away from Touchline Manager, your tenure at {clubName} has ended and the
         club is AI-controlled again.

         Its squad, finances, and history are unchanged. You are welcome to take over another club.
         """,
        $"""
         <p>After {daysInactive} days away from Touchline Manager, your tenure at <strong>{clubName}</strong>
         has ended and the club is AI-controlled again.</p>
         <p>Its squad, finances, and history are unchanged. You are welcome to take over another club.</p>
         """);

    /// <summary>Builds the reminder that a matchday's team sheet locks soon.</summary>
    /// <param name="to">The manager's address.</param>
    /// <param name="clubName">The club they hold.</param>
    /// <param name="roundNumber">The round about to lock.</param>
    /// <param name="opponentName">The opponent's generated name.</param>
    /// <param name="lockAt">When the team sheet locks.</param>
    public static EmailMessage DeadlineReminder(
        string to,
        string clubName,
        int roundNumber,
        string opponentName,
        DateTimeOffset lockAt) => new(
        to,
        $"Round {roundNumber}: submit your team sheet for {clubName}",
        $"""
         Your team sheet for {clubName} locks at {lockAt:u}.

         Set your side for round {roundNumber} before then, or the AI will decide any places you leave.
         """,
        $"""
         <p>Your team sheet for <strong>{clubName}</strong> locks at {lockAt:u}.</p>
         <p>Set your side for round {roundNumber} before then, or the AI will decide any places you leave.</p>
         """);
}

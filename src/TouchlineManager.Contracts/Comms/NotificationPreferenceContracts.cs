namespace TouchlineManager.Contracts.Comms;

/// <summary>A manager's notification preferences (`COM-4`, master plan §10.7).</summary>
/// <remarks>
/// Every switch defaults on, so a manager who has never opened the screen still receives the notifications the
/// game means to send. <paramref name="Version"/> is the concurrency token the client returns in
/// <c>If-Match</c> when it changes them (`CONC-1`).
/// </remarks>
/// <param name="EmailDeadlineReminders">Whether to email before a matchday's team sheet locks.</param>
/// <param name="EmailInactivityWarnings">Whether to email when a tenure is about to go inactive.</param>
/// <param name="EmailMarketMessages">Whether to email about transfer-market events.</param>
/// <param name="EmailNewsDigest">Whether to email the division news digest.</param>
/// <param name="Version">The optimistic concurrency version.</param>
/// <param name="ServerTime">The server's current instant (`TIME-5`).</param>
public sealed record NotificationPreferencesResponse(
    bool EmailDeadlineReminders,
    bool EmailInactivityWarnings,
    bool EmailMarketMessages,
    bool EmailNewsDigest,
    long Version,
    DateTimeOffset ServerTime);

/// <summary>The body of a notification-preferences change (`COM-4`).</summary>
/// <param name="EmailDeadlineReminders">Whether to email before a matchday's team sheet locks.</param>
/// <param name="EmailInactivityWarnings">Whether to email when a tenure is about to go inactive.</param>
/// <param name="EmailMarketMessages">Whether to email about transfer-market events.</param>
/// <param name="EmailNewsDigest">Whether to email the division news digest.</param>
public sealed record UpdateNotificationPreferencesRequest(
    bool EmailDeadlineReminders,
    bool EmailInactivityWarnings,
    bool EmailMarketMessages,
    bool EmailNewsDigest);

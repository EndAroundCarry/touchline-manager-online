namespace Tactical11Manager.Contracts.Forum;

/// <summary>Request to create a new forum thread.</summary>
/// <param name="Title">Thread title (3 to 150 characters).</param>
/// <param name="Body">Initial post body (3 to 10,000 characters).</param>
public sealed record CreateForumThreadRequest(string Title, string Body);

/// <summary>Request to reply to an existing forum thread.</summary>
/// <param name="Body">Reply post body (1 to 10,000 characters).</param>
public sealed record CreateForumPostRequest(string Body);

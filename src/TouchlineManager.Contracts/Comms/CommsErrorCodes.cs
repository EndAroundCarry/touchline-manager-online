namespace TouchlineManager.Contracts.Comms;

/// <summary>
/// The stable refusal codes the inbox surface returns (master plan §10.7).
/// </summary>
/// <remarks>
/// A client branches on these rather than on prose. They are separate from the squad, world, and competition
/// codes because they answer the inbox's own questions: whether a message is one the caller may see, and
/// whether the page cursor is one this build produced.
/// </remarks>
public static class CommsErrorCodes
{
    /// <summary>No message exists with the requested identity for this manager.</summary>
    public const string MessageNotFound = "MESSAGE_NOT_FOUND";

    /// <summary>The page cursor was not produced by this build.</summary>
    public const string InvalidCursor = "INVALID_CURSOR";
}

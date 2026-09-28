namespace TouchlineManager.Application.Comms;

/// <summary>The outbox message kinds this build can dispatch (`MOD-4`).</summary>
/// <remarks>
/// A stable token, like a template key, so a row written today is understood by a dispatcher built later;
/// a type no dispatcher knows is dead-lettered rather than retried forever.
/// </remarks>
public static class OutboxTypes
{
    /// <summary>An email to send through the transactional provider.</summary>
    public const string Email = "email";

    /// <summary>The aggregate type recorded for an email, which has no domain aggregate of its own.</summary>
    public const string EmailAggregateType = "email";
}

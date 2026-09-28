using System.Globalization;

namespace TouchlineManager.Application.Jobs;

/// <summary>The outbox dispatcher's job identity (`MOD-4`, ADR-0028).</summary>
public static class OutboxJobTypes
{
    /// <summary>Drains the outbox's due messages.</summary>
    public const string Dispatch = "ops.dispatch-outbox";

    /// <summary>
    /// The business key for a minute: a completed dispatch row is terminal, so the key must advance for the
    /// next pass to enqueue, and the minute is the coarsest bucket at which dispatch is meant to run.
    /// </summary>
    /// <param name="now">The current instant.</param>
    public static string MinuteKey(DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"{Dispatch}:{now:yyyyMMddHHmm}");
}

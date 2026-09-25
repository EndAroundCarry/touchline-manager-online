using System.Globalization;
using System.Text.Json;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// The three job types a matchday's life is driven by, and the keys that keep each one unique
/// (master plan §7.2, ADR-0003).
/// </summary>
/// <remarks>
/// <para>
/// One round is locked, resolved, and published, and each of the three is a durable row with a business
/// key derived from the matchday's identity. The key is what makes the queue's at-least-once delivery safe:
/// a materialiser that runs every few minutes, a worker that restarts mid-job, and an operator who retries
/// a dead letter all enqueue the same three keys, and the database refuses the second insertion.
/// </para>
/// <para>
/// Publication is enqueued by resolution rather than by the materialiser, because it is only meaningful
/// once the round is staged: a job that says "publish this round" before the round has results would have
/// to be rescheduled, and a job that waits for a condition it cannot express is a job that needs a
/// materialiser to poll.
/// </para>
/// </remarks>
public static class MatchdayJobTypes
{
    /// <summary>Freezes the round's team sheets and input snapshots at the lock deadline (`CAL-3`).</summary>
    public const string Lock = "competition.lock-matchday";

    /// <summary>Simulates the round's fixtures from their snapshots and stages the results (`MAT-7`).</summary>
    public const string Resolve = "competition.resolve-matchday";

    /// <summary>Publishes the round and applies its projections, all or nothing (`MAT-7`).</summary>
    public const string Publish = "competition.publish-matchday";

    /// <summary>Gets the business key of a round's lock job.</summary>
    /// <param name="matchdayId">The matchday.</param>
    public static string LockKey(Guid matchdayId) => KeyFor(matchdayId, "lock");

    /// <summary>Gets the business key of a round's resolution job.</summary>
    /// <param name="matchdayId">The matchday.</param>
    public static string ResolveKey(Guid matchdayId) => KeyFor(matchdayId, "resolve");

    /// <summary>Gets the business key of a round's publication job.</summary>
    /// <param name="matchdayId">The matchday.</param>
    public static string PublishKey(Guid matchdayId) => KeyFor(matchdayId, "publish");

    private static string KeyFor(Guid matchdayId, string step) =>
        string.Create(CultureInfo.InvariantCulture, $"matchday:{matchdayId:D}:{step}");
}

/// <summary>The payload every matchday job carries: which round it is for.</summary>
public static class MatchdayJobPayload
{
    /// <summary>Builds the payload that names the round a job is for.</summary>
    /// <param name="matchdayId">The matchday.</param>
    public static string For(Guid matchdayId) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"matchdayId":"{{matchdayId:D}}"}""");

    /// <summary>Reads the round from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="matchdayId">The matchday, when the payload names one.</param>
    /// <returns>Whether the payload named a matchday.</returns>
    public static bool TryRead(string payloadJson, out Guid matchdayId)
    {
        matchdayId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);

            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("matchdayId", out var value)
                && Guid.TryParse(value.GetString(), out matchdayId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

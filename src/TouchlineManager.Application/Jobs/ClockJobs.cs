using System.Globalization;
using System.Text.Json;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// The job type an operator's step runs, and the key that keeps one step per target unique (ADR-0049).
/// </summary>
/// <remarks>
/// <para>
/// A step is a real durable job rather than a change the endpoint makes, so the worker remains the only
/// component that moves the game forward (ADR-0001, ADR-0008). The endpoint resolves which instant to step
/// to — the next day, or the next round's kickoff — and enqueues the job due now; the worker sets the stored
/// instant and materialises the day's jobs (ADR-0016's pattern, applied to time).
/// </para>
/// <para>
/// The business key is derived from the target instant, so a repeated request for the same target enqueues
/// once, and a request for a further target is a new row.
/// </para>
/// </remarks>
public static class ClockJobTypes
{
    /// <summary>Advances the stored game instant and materialises the day it lands on (ADR-0049).</summary>
    public const string Advance = "ops.advance-game-clock";

    /// <summary>Gets the business key of a step to a target instant.</summary>
    /// <param name="target">The step kind, <see cref="ClockJobPayload.Day"/> or <see cref="ClockJobPayload.Matchday"/>.</param>
    /// <param name="targetInstantUtc">The instant the step lands on.</param>
    public static string AdvanceKey(string target, DateTimeOffset targetInstantUtc) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"clock:{target}:{targetInstantUtc.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffffffZ}");
}

/// <summary>The payload a clock step carries: the instant to land on.</summary>
public static class ClockJobPayload
{
    /// <summary>Step to the start of the next game day.</summary>
    public const string Day = "day";

    /// <summary>Step to the next round that has not yet been played, falling back to a day.</summary>
    public const string Matchday = "matchday";

    /// <summary>Builds the payload naming the instant to step to.</summary>
    /// <param name="targetInstantUtc">The instant the step lands on.</param>
    public static string For(DateTimeOffset targetInstantUtc) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"targetInstant":"{{targetInstantUtc.ToUniversalTime():O}}"}""");

    /// <summary>Reads the target instant from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="targetInstantUtc">The instant, when the payload named one.</param>
    /// <returns>Whether the payload named an instant.</returns>
    public static bool TryReadTargetInstant(string payloadJson, out DateTimeOffset targetInstantUtc)
    {
        targetInstantUtc = default;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);

            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("targetInstant", out var value)
                && DateTimeOffset.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out targetInstantUtc);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

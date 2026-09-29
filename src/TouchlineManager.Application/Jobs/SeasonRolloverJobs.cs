using System.Globalization;
using System.Text.Json;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// The job that rolls a season over (`PR-4`, master plan §7.2, ADR-0031).
/// </summary>
/// <remarks>
/// One durable row with a business key derived from the closing season — <c>season:{id}:rollover</c>. The key
/// is what makes the queue's at-least-once delivery safe: a materialiser that runs every few minutes, a
/// worker that restarts mid-job, and a redelivery after a transient failure all enqueue the same key, and the
/// database refuses the second insertion. The <see cref="SeasonRollover"/> row it advances is the checkpoint
/// the resumable state machine reads.
/// </remarks>
public static class SeasonRolloverJobTypes
{
    /// <summary>Freezes, finalizes, moves, and completes one season's rollover (master plan §7.5).</summary>
    public const string Rollover = "competition.season-rollover";

    /// <summary>Gets the business key of a season's rollover job.</summary>
    /// <param name="seasonId">The closing season.</param>
    public static string RolloverKey(Guid seasonId) =>
        string.Create(CultureInfo.InvariantCulture, $"season:{seasonId:D}:rollover");
}

/// <summary>The payload the rollover job carries: which season it closes.</summary>
public static class SeasonRolloverJobPayload
{
    /// <summary>Builds the payload that names the season a job is for.</summary>
    /// <param name="seasonId">The closing season.</param>
    public static string For(Guid seasonId) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"seasonId":"{{seasonId:D}}"}""");

    /// <summary>Reads the season from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="seasonId">The season, when the payload names one.</param>
    /// <returns>Whether the payload named a season.</returns>
    public static bool TryRead(string payloadJson, out Guid seasonId)
    {
        seasonId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);

            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("seasonId", out var value)
                && Guid.TryParse(value.GetString(), out seasonId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

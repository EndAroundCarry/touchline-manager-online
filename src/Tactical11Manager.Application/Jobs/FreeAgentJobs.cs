using System.Globalization;
using System.Text.Json;

namespace Tactical11Manager.Application.Jobs;

/// <summary>
/// The one job the weekly free-agent pool is driven by, and the key that keeps it unique (`FA-5`,
/// master plan §7.2, ADR-0003).
/// </summary>
/// <remarks>
/// A weekly row keyed on the Monday it opens on, the same shape the weekly finance run uses: the row is the
/// deadline, so a worker that was down at Monday noon runs the batch late rather than skipping it, and the
/// batch row's own uniqueness makes the at-least-once queue safe.
/// </remarks>
public static class FreeAgentJobTypes
{
    /// <summary>Opens a week's free-agent pool: generates, picks up the released, and lists them all.</summary>
    public const string Batch = "market.free-agent-batch";

    /// <summary>Gets the business key of a week's batch job.</summary>
    /// <param name="weekStart">The Monday the batch opens on.</param>
    public static string WeekKey(DateOnly weekStart) =>
        string.Create(CultureInfo.InvariantCulture, $"{Batch}:{weekStart:yyyy-MM-dd}");

    /// <summary>Builds the payload that names the week a job is for.</summary>
    /// <param name="weekStart">The Monday the batch opens on.</param>
    public static string PayloadFor(DateOnly weekStart) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"weekStart":"{{weekStart:yyyy-MM-dd}}"}""");

    /// <summary>Reads the week from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="weekStart">The week, when the payload names one.</param>
    /// <returns>Whether the payload named a week.</returns>
    public static bool TryReadWeek(string payloadJson, out DateOnly weekStart)
    {
        weekStart = default;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);

            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("weekStart", out var value)
                && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out weekStart);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

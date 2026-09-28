using System.Globalization;
using System.Text.Json;

namespace TouchlineManager.Application.Jobs;

/// <summary>The deadline reminder's job identity (`COM-3`, ADR-0029).</summary>
public static class ReminderJobTypes
{
    /// <summary>Writes the deadline reminder for one round.</summary>
    public const string Deadline = "comms.deadline-reminder";

    /// <summary>The business key for a round's reminder, so a round is reminded once.</summary>
    /// <param name="matchdayId">The round.</param>
    public static string MatchdayKey(Guid matchdayId) =>
        string.Create(CultureInfo.InvariantCulture, $"matchday:{matchdayId:D}:reminder");
}

/// <summary>The deadline reminder job's payload (`COM-3`).</summary>
public static class ReminderJobPayload
{
    /// <summary>Builds the payload for a round.</summary>
    /// <param name="matchdayId">The round.</param>
    public static string For(Guid matchdayId) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"matchdayId":"{{matchdayId:D}}"}""");

    /// <summary>Reads the round back from a payload.</summary>
    /// <param name="payloadJson">The payload.</param>
    /// <param name="matchdayId">The round, when the payload was readable.</param>
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

using System.Globalization;
using System.Text.Json;

namespace Tactical11Manager.Application.Jobs;

/// <summary>
/// The job that signs the sponsor deals a division's managers did not sign by the deadline, and the key that keeps it unique (`SPN-4`, ADR-0003).
/// </summary>
/// <remarks>
/// The key names the division-season, so the materialiser can ensure it on every pass and the queue's unique business key refuses the
/// duplicates. It is due at the fourth matchday's team-sheet lock.
/// </remarks>
public static class SponsorJobTypes
{
    /// <summary>Signs the board's deal for every club of a division-season that has not signed (`SPN-4`).</summary>
    public const string SignDeals = "finance.sign-sponsor-deals";

    /// <summary>Gets the business key of a division-season's deadline job.</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    public static string SignDealsKey(Guid divisionSeasonId) =>
        string.Create(CultureInfo.InvariantCulture, $"sponsor-deadline:{divisionSeasonId:D}");

    /// <summary>Builds the payload that names the division-season.</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    public static string PayloadFor(Guid divisionSeasonId) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"divisionSeasonId":"{{divisionSeasonId:D}}"}""");

    /// <summary>Reads the division-season from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="divisionSeasonId">The division-season, when the payload names one.</param>
    /// <returns>Whether the payload named one.</returns>
    public static bool TryReadPayload(string payloadJson, out Guid divisionSeasonId)
    {
        divisionSeasonId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);

            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("divisionSeasonId", out var value)
                && Guid.TryParse(value.GetString(), out divisionSeasonId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

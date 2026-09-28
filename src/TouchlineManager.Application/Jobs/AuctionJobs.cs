using System.Globalization;
using System.Text.Json;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// The one job a listing's life is driven by, and the key that keeps it unique (`TRF-2`, master plan §7.2,
/// ADR-0003).
/// </summary>
/// <remarks>
/// Resolution is a durable row with a business key derived from the listing's identity — <c>listing:{id}:resolve</c>,
/// the key ADR-0003 names. The key is what makes the queue's at-least-once delivery safe: a materialiser that
/// runs every few minutes, a worker that restarts mid-job, and an operator who retries a dead letter all
/// enqueue the same key, and the database refuses the second insertion.
/// </remarks>
public static class AuctionJobTypes
{
    /// <summary>Resolves a listing at its window, settling the winning bid (`TRF-9`, `TRF-10`).</summary>
    public const string Resolve = "market.resolve-auction";

    /// <summary>Gets the business key of a listing's resolution job.</summary>
    /// <param name="listingId">The listing.</param>
    public static string ResolveKey(Guid listingId) =>
        string.Create(CultureInfo.InvariantCulture, $"listing:{listingId:D}:resolve");
}

/// <summary>The payload the auction resolution job carries: which listing it is for.</summary>
public static class AuctionJobPayload
{
    /// <summary>Builds the payload that names the listing a job is for.</summary>
    /// <param name="listingId">The listing.</param>
    public static string For(Guid listingId) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"listingId":"{{listingId:D}}"}""");

    /// <summary>Reads the listing from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="listingId">The listing, when the payload names one.</param>
    /// <returns>Whether the payload named a listing.</returns>
    public static bool TryRead(string payloadJson, out Guid listingId)
    {
        listingId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);

            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("listingId", out var value)
                && Guid.TryParse(value.GetString(), out listingId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

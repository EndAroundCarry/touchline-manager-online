using System.Globalization;
using System.Text.Json;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// The job that reconciles and rebuilds a division's projections (`TBL-13`, master plan §7.2).
/// </summary>
/// <remarks>
/// A repair, not a deadline: it has no calendar of its own and is enqueued by an operator or a recovery
/// workflow when a projection is suspected of having drifted. Its business key is derived from the
/// division-season, so enqueuing the same repair twice is a no-op and a retried job is idempotent — the
/// rebuild itself writes only what differs (ADR-0003).
/// </remarks>
public static class ProjectionJobTypes
{
    /// <summary>Recomputes a division-season's table and season statistics from its published results.</summary>
    public const string RebuildDivisionProjections = "competition.rebuild-division-projections";

    /// <summary>Gets the business key of a division-season's projection rebuild.</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    public static string RebuildDivisionProjectionsKey(Guid divisionSeasonId) =>
        string.Create(CultureInfo.InvariantCulture, $"division-season:{divisionSeasonId:D}:rebuild-projections");
}

/// <summary>The payload the projection rebuild carries: which division-season it is for.</summary>
public static class ProjectionJobPayload
{
    /// <summary>Builds the payload that names the division-season a job is for.</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    public static string For(Guid divisionSeasonId) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"divisionSeasonId":"{{divisionSeasonId:D}}"}""");

    /// <summary>Reads the division-season from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="divisionSeasonId">The division-season, when the payload names one.</param>
    /// <returns>Whether the payload named a division-season.</returns>
    public static bool TryRead(string payloadJson, out Guid divisionSeasonId)
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

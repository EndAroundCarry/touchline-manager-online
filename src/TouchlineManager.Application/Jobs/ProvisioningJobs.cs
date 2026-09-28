using System.Globalization;
using System.Text.Json;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// The job that generates a country's next tier (`PYR-4`, master plan §7.2, ADR-0005).
/// </summary>
/// <remarks>
/// A durable row with a business key derived from the country and the target tier — <c>country:{id}:tier:{n}:provision</c>,
/// the key ADR-0003 names. The key is what makes the queue's at-least-once delivery safe: a materialiser that
/// runs every few minutes, a worker that restarts mid-job, and an operator who retries a dead letter all
/// enqueue the same key, and the database refuses the second insertion.
/// </remarks>
public static class ProvisioningJobTypes
{
    /// <summary>Generates, backfills, validates, and activates a requested tier (`PYR-4`…`PYR-8`).</summary>
    public const string Provision = "world.provision-division";

    /// <summary>Gets the business key of a tier's provisioning job.</summary>
    /// <param name="countryId">The country.</param>
    /// <param name="targetTier">The tier being generated.</param>
    public static string ProvisionKey(Guid countryId, int targetTier) =>
        string.Create(CultureInfo.InvariantCulture, $"country:{countryId:D}:tier:{targetTier}:provision");
}

/// <summary>The payload the provisioning job carries: which request it is for.</summary>
public static class ProvisioningJobPayload
{
    /// <summary>Builds the payload that names the request a job is for.</summary>
    /// <param name="requestId">The provisioning request.</param>
    public static string For(Guid requestId) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"requestId":"{{requestId:D}}"}""");

    /// <summary>Reads the request from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="requestId">The request, when the payload names one.</param>
    /// <returns>Whether the payload named a request.</returns>
    public static bool TryRead(string payloadJson, out Guid requestId)
    {
        requestId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);

            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("requestId", out var value)
                && Guid.TryParse(value.GetString(), out requestId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

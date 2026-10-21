using System.Globalization;
using System.Text.Json;

namespace Tactical11Manager.Application.Jobs;

/// <summary>
/// The one job an upgrade's build time is driven by, and the key that keeps it unique (`STRC-3`, ADR-0003).
/// </summary>
/// <remarks>
/// The key names the building and the level the work reaches, so an upgrade to level 3 is a different job
/// from the upgrade to level 2, and the queue's at-least-once delivery cannot finish one upgrade twice.
/// </remarks>
public static class StructureJobTypes
{
    /// <summary>Finishes a building's upgrade and raises its level (`STRC-3`).</summary>
    public const string CompleteUpgrade = "world.complete-structure-upgrade";

    /// <summary>Gets the business key of an upgrade's completion job.</summary>
    /// <param name="structureId">The building.</param>
    /// <param name="targetLevel">The level the work reaches.</param>
    public static string CompleteUpgradeKey(Guid structureId, int targetLevel) =>
        string.Create(CultureInfo.InvariantCulture, $"structure:{structureId:D}:level:{targetLevel}");
}

/// <summary>The payload the upgrade completion job carries: which building and which level.</summary>
public static class StructureJobPayload
{
    /// <summary>Builds the payload that names the building and the level the job completes.</summary>
    /// <param name="structureId">The building.</param>
    /// <param name="targetLevel">The level the work reaches.</param>
    public static string For(Guid structureId, int targetLevel) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"structureId":"{{structureId:D}}","targetLevel":{{targetLevel}}}""");

    /// <summary>Reads the building and the level from a payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="structureId">The building, when the payload names one.</param>
    /// <param name="targetLevel">The level, when the payload names one.</param>
    /// <returns>Whether the payload named both.</returns>
    public static bool TryRead(string payloadJson, out Guid structureId, out int targetLevel)
    {
        structureId = Guid.Empty;
        targetLevel = 0;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);
            var root = payload.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("structureId", out var id)
                && Guid.TryParse(id.GetString(), out structureId)
                && root.TryGetProperty("targetLevel", out var level)
                && level.TryGetInt32(out targetLevel);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

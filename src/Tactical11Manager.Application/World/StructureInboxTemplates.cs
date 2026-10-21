using System.Text.Json;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Domain.Comms;

namespace Tactical11Manager.Application.World;

/// <summary>
/// The inbox template for a finished upgrade of the snack bar or the merchandise shop (`STRC-3`).
/// </summary>
/// <remarks>
/// The same durable-token contract the rest of the inbox uses (`MAT-8`): a stable key and a parameter
/// document, rendered by <see cref="InboxMessageText"/>. It sits on the system shelf because the message
/// names no match, player or squad, so nothing on the other shelves is the place a manager would look for it.
/// </remarks>
public static class StructureInboxTemplates
{
    /// <summary>A building's upgrade finished and its level rose (`STRC-3`).</summary>
    public const string UpgradeCompleted = "inbox.structure.upgrade_completed";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the message a manager receives when an upgrade finishes.</summary>
    /// <param name="kindCode">The building's stable code.</param>
    /// <param name="level">The level the building has reached.</param>
    /// <param name="structureId">The building, which the message is about.</param>
    public static InboxDraft UpgradeCompletedMessage(string kindCode, int level, Guid structureId) =>
        new(
            InboxCategory.System,
            UpgradeCompleted,
            JsonSerializer.Serialize(new UpgradeCompletedParameters(kindCode, level), Options),
            structureId);

    /// <summary>Reads a stored parameter document back.</summary>
    internal static UpgradeCompletedParameters Read(string parametersJson) =>
        JsonSerializer.Deserialize<UpgradeCompletedParameters>(parametersJson, Options)
        ?? throw new InvalidOperationException("A stored inbox message carries no parameters.");

    internal sealed record UpgradeCompletedParameters(string Kind, int Level);
}

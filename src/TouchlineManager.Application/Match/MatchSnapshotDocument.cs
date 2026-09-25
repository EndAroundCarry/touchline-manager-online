using System.Text.Json;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Match;

/// <summary>The frozen input as it is stored: the engine's input, and the decisions made to build it.</summary>
/// <param name="Input">The engine's input.</param>
/// <param name="Repairs">Every slot the builder decided, in slot order.</param>
public sealed record MatchSnapshotContent(MatchInputV1 Input, IReadOnlyList<SnapshotRepair> Repairs);

/// <summary>
/// Writes and reads the versioned document a snapshot's JSONB column holds (`MAT-1`, §4.5, §6.6).
/// </summary>
/// <remarks>
/// <para>
/// The stored document is the frozen engine input plus the repair decisions that produced it. Master plan
/// §6.6 requires both — the snapshot includes "suspensions repair decisions" — and the engine's own
/// contract has nowhere to put them, because a repair is a fact about selection rather than about a match.
/// </para>
/// <para>
/// A schema discriminator is mandatory for a stored document (§4.5), and reading refuses any version other
/// than this one: a snapshot written by a later engine is not something this build can honestly simulate.
/// The document round-trips exactly, which is what the lock workflow verifies against the snapshot hash
/// before it simulates anything — a document that did not reproduce its hash would be a silent rewrite of
/// the facts a result claims to have been produced from (`MAT-9`).
/// </para>
/// </remarks>
public static class MatchSnapshotDocument
{
    /// <summary>The document's schema discriminator.</summary>
    public const string Schema = "match-snapshot-v1";

    /// <summary>Writes the document.</summary>
    /// <param name="input">The engine's input, with its seed attached.</param>
    /// <param name="repairs">Every repair the builder made.</param>
    public static string Write(MatchInputV1 input, IReadOnlyList<SnapshotRepair> repairs)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(repairs);

        var document = new SnapshotDocument(
            Schema,
            input,
            [.. repairs.Select(repair => new RepairDocument(
                repair.ClubId,
                repair.SlotNumber,
                repair.Reason.ToCode(),
                repair.ReplacedPlayerId,
                repair.ReplacementPlayerId))]);

        return JsonSerializer.Serialize(document, MatchJson.Options);
    }

    /// <summary>Reads the document.</summary>
    /// <param name="json">The stored document.</param>
    /// <returns>The engine's input and the repairs that produced it.</returns>
    /// <exception cref="InvalidMatchInputException">When the document is not this schema, or is unreadable.</exception>
    public static MatchSnapshotContent Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        SnapshotDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<SnapshotDocument>(json, MatchJson.Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidMatchInputException("The stored match snapshot is not readable.", exception);
        }

        if (document is null || !string.Equals(document.Schema, Schema, StringComparison.Ordinal))
        {
            throw new InvalidMatchInputException(
                $"The stored match snapshot is not a '{Schema}' document.");
        }

        if (document.Input is null)
        {
            throw new InvalidMatchInputException("The stored match snapshot carries no input.");
        }

        return new MatchSnapshotContent(
            document.Input,
            [.. (document.Repairs ?? []).Select(repair => new SnapshotRepair(
                repair.ClubId,
                repair.SlotNumber,
                SnapshotRepairReasons.FromCode(repair.Reason),
                repair.ReplacedPlayerId,
                repair.ReplacementPlayerId))]);
    }

    /// <summary>The stored document's shape, whose member names are camelCase through the web defaults.</summary>
    private sealed record SnapshotDocument(
        string Schema,
        MatchInputV1 Input,
        IReadOnlyList<RepairDocument> Repairs);

    /// <summary>One repair, with its reason as the stable code rather than as an ordinal.</summary>
    private sealed record RepairDocument(
        Guid ClubId,
        int SlotNumber,
        string Reason,
        Guid? ReplacedPlayerId,
        Guid? ReplacementPlayerId);
}

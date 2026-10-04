using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TouchlineManager.Domain.Squad.Training;

/// <summary>One attribute's movement of progress across a training day (`TRN-10`).</summary>
/// <param name="Attribute">The attribute whose progress moved.</param>
/// <param name="DeltaMicro">
/// The net movement in millionths of a point: positive for the share of the day's development the attribute
/// earned, negative for the decline it incurred. Never zero.
/// </param>
public sealed record AttributeProgressChange(AttributeName Attribute, int DeltaMicro);

/// <summary>
/// The progress each attribute has made towards its next point, in millionths of a point (`TRN-10`, `TRN-16`).
/// </summary>
/// <remarks>
/// <para>
/// Progress is signed. A trained attribute accumulates a positive share of the day's development, an ageing
/// one accumulates decline, and the two net against each other. When it reaches a whole point (1,000,000) the
/// attribute rises and the point is taken off; when it reaches minus a whole point the attribute falls. The
/// stored value is therefore always strictly between minus one and one point.
/// </para>
/// <para>
/// It is stored as compact JSON, <c>[[attributeIndex, micro], ...]</c> in canonical attribute order, holding
/// only the attributes with progress, because most of a player's attributes carry none.
/// </para>
/// </remarks>
public static class AttributeProgress
{
    /// <summary>How many millionths make one displayed attribute point.</summary>
    public const int Basis = 1_000_000;

    /// <summary>The stored form of a player with no progress on any attribute.</summary>
    public const string EmptyJson = "[]";

    /// <summary>Gets a progress set with no progress on any attribute.</summary>
    public static IReadOnlyList<int> None() => new int[AttributeNames.Count];

    /// <summary>Gets whether every value is a valid stored progress: one per attribute, inside one point.</summary>
    /// <param name="progress">The progress, in canonical attribute order.</param>
    public static bool IsValid(IReadOnlyList<int> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return progress.Count == AttributeNames.Count
            && progress.All(value => value > -Basis && value < Basis);
    }

    /// <summary>Renders a progress set as <c>[[attributeIndex, micro], ...]</c>, leaving out the zeros.</summary>
    /// <param name="progress">The progress, in canonical attribute order.</param>
    public static string Serialize(IReadOnlyList<int> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var builder = new StringBuilder("[");
        var first = true;

        for (var index = 0; index < progress.Count; index++)
        {
            if (progress[index] == 0)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder
                .Append('[')
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(progress[index].ToString(CultureInfo.InvariantCulture))
                .Append(']');
        }

        return builder.Append(']').ToString();
    }

    /// <summary>Renders a day's movements in the same compact form.</summary>
    /// <param name="changes">The movements, in canonical attribute order.</param>
    public static string Serialize(IReadOnlyList<AttributeProgressChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var progress = new int[AttributeNames.Count];

        foreach (var change in changes)
        {
            progress[(int)change.Attribute] = change.DeltaMicro;
        }

        return Serialize(progress);
    }

    /// <summary>Reads the compact form back into one value per attribute.</summary>
    /// <param name="json">The stored JSON.</param>
    public static int[] Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var progress = new int[AttributeNames.Count];

        foreach (var pair in JsonSerializer.Deserialize<int[][]>(json) ?? [])
        {
            progress[pair[0]] = pair[1];
        }

        return progress;
    }

    /// <summary>Reads the compact form back as movements, in canonical attribute order.</summary>
    /// <param name="json">The stored JSON.</param>
    public static IReadOnlyList<AttributeProgressChange> ParseChanges(string json)
    {
        var progress = Parse(json);

        return
        [
            .. AttributeNames.All
                .Where(name => progress[(int)name] != 0)
                .Select(name => new AttributeProgressChange(name, progress[(int)name])),
        ];
    }
}

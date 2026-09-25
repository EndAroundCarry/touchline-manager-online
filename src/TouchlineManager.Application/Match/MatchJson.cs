using System.Text.Json;
using System.Text.Json.Serialization;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Match;

/// <summary>
/// The serializer settings every stored match document is written and read with (master plan §4.5).
/// </summary>
/// <remarks>
/// <para>
/// Documents are camelCase and indented so a stored row can be read and diffed by a person investigating a
/// result; the indentation costs bytes that nothing queries on, and the alternative is a single line of
/// JSON in a database console.
/// </para>
/// <para>
/// Enums are written as numbers on purpose. The engine's vocabulary is a pinned contract whose values are
/// part of the input hash, so a document that said <c>"LeftWinger"</c> would be a second name for the same
/// number and a chance for the two to disagree. The one exception is the repair reason in a snapshot
/// document, which carries its stable code because it is not part of any hash.
/// </para>
/// <para>
/// Unknown members are ignored on the way in, so a document written by a later build that added a field can
/// still be read by this one; a document whose <c>schema</c> is not the one expected is refused instead,
/// because a newer schema may mean something different by the same field name.
/// </para>
/// </remarks>
internal static class MatchJson
{
    /// <summary>Gets the settings every match document uses.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        // The attribute set has no public constructor: it can only be built through its own factory, which
        // validates the scale and the count. Round-tripping through that factory is what makes a stored
        // snapshot's attributes re-validated on the way in rather than trusted.
        options.Converters.Add(new PlayerAttributesConverter());

        return options;
    }

    /// <summary>Reads and writes the engine's attribute set, which only its own factory may construct.</summary>
    private sealed class PlayerAttributesConverter : JsonConverter<PlayerAttributesV1>
    {
        public override PlayerAttributesV1 Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<IReadOnlyList<int>>(ref reader, options)
                ?? throw new JsonException("A player's attributes are a list of values.");

            return PlayerAttributesV1.From(values);
        }

        public override void Write(
            Utf8JsonWriter writer,
            PlayerAttributesV1 value,
            JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Values, options);
    }
}

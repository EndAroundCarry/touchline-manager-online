using System.Text.Json;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Match;

/// <summary>A match's stored statistics: both sides, how long was played, and who played it.</summary>
/// <param name="Home">The home side's statistics.</param>
/// <param name="Away">The away side's statistics.</param>
/// <param name="TotalMinutesPlayed">Regulation plus stoppage, as the engine played it.</param>
/// <param name="PlayerLines">Every participant's line, which carries the minutes they played.</param>
public sealed record MatchStatisticsContent(
    MatchStatisticsV1 Home,
    MatchStatisticsV1 Away,
    int TotalMinutesPlayed,
    IReadOnlyList<MatchPlayerLineV1> PlayerLines);

/// <summary>
/// Writes and reads the versioned statistics document a stored match holds (`MAT-5`, §4.5, §6.6).
/// </summary>
/// <remarks>
/// <para>
/// The engine derives every count here from the event stream, so the document and the match's events agree
/// by construction and the reconciliation rule cannot be violated by a second accumulator (`MAT-5`).
/// </para>
/// <para>
/// It is a document rather than thirty-four columns because nothing filters or sorts on a statistic: the
/// only reader is the match viewer, which shows all of them at once. Anything the world does query — a
/// scoreline, a card, a table row — is a column somewhere else.
/// </para>
/// <para>
/// The player lines are stored beside the team statistics because publication applies a match's effects on
/// the squad — condition consumed, fatigue accumulated, morale moved (`TRN-11`, `TRN-13`) — and those are
/// decided from the minutes each player actually played. Storing them means a delayed publication reads the
/// facts the result was made from rather than re-simulating under whatever engine build has since shipped,
/// which is the same argument that keeps the statistics document rather than re-deriving the summary.
/// </para>
/// </remarks>
public static class MatchStatisticsDocument
{
    /// <summary>The document's schema discriminator.</summary>
    /// <remarks>
    /// Version 2 added the player lines; version 3 added the assists and the match rating those lines now
    /// carry (`engine-v2`); version 4 adds each player's final condition, substitution minutes, injury flag,
    /// and live rating (`engine-v3`), which the match center reads; version 5 adds the passes and take-ons each
    /// line counts (`engine-v6`), which the season statistics sum. The version is bumped rather than read
    /// leniently for the same reason each time: a document of an older shape would be accepted with a field
    /// missing, so a reader that cannot honour it refuses it by name instead (`JSN-5`).
    /// </remarks>
    public const string Schema = "match-statistics-v5";

    /// <summary>Writes a result's statistics.</summary>
    /// <param name="result">The engine's result.</param>
    public static string Write(MatchResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var document = new StatisticsDocument(
            Schema,
            result.Home,
            result.Away,
            result.TotalMinutesPlayed,
            result.PlayerLines);

        return JsonSerializer.Serialize(document, MatchJson.Options);
    }

    /// <summary>Reads a stored statistics document.</summary>
    /// <param name="json">The stored document.</param>
    /// <exception cref="InvalidMatchInputException">When the document is not this schema, or is unreadable.</exception>
    public static MatchStatisticsContent Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        StatisticsDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<StatisticsDocument>(json, MatchJson.Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidMatchInputException("The stored match statistics are not readable.", exception);
        }

        if (document is null || !string.Equals(document.Schema, Schema, StringComparison.Ordinal))
        {
            throw new InvalidMatchInputException(
                $"The stored match statistics are not a '{Schema}' document.");
        }

        if (document.Home is null || document.Away is null)
        {
            throw new InvalidMatchInputException("The stored match statistics carry only one side.");
        }

        return new MatchStatisticsContent(
            document.Home,
            document.Away,
            document.TotalMinutesPlayed,
            document.PlayerLines ?? []);
    }

    /// <summary>The stored document's shape.</summary>
    private sealed record StatisticsDocument(
        string Schema,
        MatchStatisticsV1? Home,
        MatchStatisticsV1? Away,
        int TotalMinutesPlayed,
        IReadOnlyList<MatchPlayerLineV1>? PlayerLines);
}

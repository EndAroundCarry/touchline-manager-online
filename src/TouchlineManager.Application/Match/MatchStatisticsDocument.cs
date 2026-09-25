using System.Text.Json;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Match;

/// <summary>A match's stored statistics: both sides, and how long was played.</summary>
/// <param name="Home">The home side's statistics.</param>
/// <param name="Away">The away side's statistics.</param>
/// <param name="TotalMinutesPlayed">Regulation plus stoppage, as the engine played it.</param>
public sealed record MatchStatisticsContent(
    MatchStatisticsV1 Home,
    MatchStatisticsV1 Away,
    int TotalMinutesPlayed);

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
/// </remarks>
public static class MatchStatisticsDocument
{
    /// <summary>The document's schema discriminator.</summary>
    public const string Schema = "match-statistics-v1";

    /// <summary>Writes a result's statistics.</summary>
    /// <param name="result">The engine's result.</param>
    public static string Write(MatchResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var document = new StatisticsDocument(
            Schema,
            result.Home,
            result.Away,
            result.TotalMinutesPlayed);

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

        return new MatchStatisticsContent(document.Home, document.Away, document.TotalMinutesPlayed);
    }

    /// <summary>The stored document's shape.</summary>
    private sealed record StatisticsDocument(
        string Schema,
        MatchStatisticsV1? Home,
        MatchStatisticsV1? Away,
        int TotalMinutesPlayed);
}

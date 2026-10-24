using System.Globalization;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Application.Comms;

/// <summary>The English a stored news item renders to: a headline and a sentence.</summary>
/// <param name="Title">The headline.</param>
/// <param name="Body">The detail.</param>
/// <param name="Spoiler">
/// What the headline and detail leave out because it would give a match away — the score — or null when the
/// item holds nothing a manager has yet to watch. A client keeps it hidden until the manager asks for it, or has
/// watched the match.
/// </param>
/// <param name="MatchId">
/// The match a result item reports, so a client can show its result once the manager has watched that match, or
/// null where the item is not about a match, or was published before the match was recorded on it.
/// </param>
public sealed record NewsText(string Title, string Body, string? Spoiler = null, Guid? MatchId = null);

/// <summary>
/// Renders a stored news item's template and parameters into English (`COM-1`, master plan §8.6).
/// </summary>
/// <remarks>
/// The counterpart of <see cref="NewsTemplates"/>, and pure like <see cref="InboxMessageText"/>: the same item
/// renders the same text on every host, and a key this build does not render is refused by name rather than
/// shown as a placeholder.
/// </remarks>
public static class NewsMessageText
{
    /// <summary>Renders one item.</summary>
    /// <param name="templateKey">The stored template key.</param>
    /// <param name="parametersJson">The stored parameter document.</param>
    /// <exception cref="InvalidOperationException">When the key is not one this build renders.</exception>
    public static NewsText Render(string templateKey, string parametersJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);

        return templateKey switch
        {
            NewsTemplates.DivisionProvisioned => Division(
                NewsTemplates.Read<NewsTemplates.DivisionParameters>(parametersJson)),
            NewsTemplates.TransferCompleted => Transfer(
                NewsTemplates.Read<NewsTemplates.TransferParameters>(parametersJson)),
            NewsTemplates.FreeAgentSigned => FreeAgent(
                NewsTemplates.Read<NewsTemplates.FreeAgentParameters>(parametersJson)),
            NewsTemplates.ResultPublished => Round(
                NewsTemplates.Read<NewsTemplates.ResultParameters>(parametersJson)),
            NewsTemplates.PlayoffDrawn => PlayoffDraw(
                NewsTemplates.Read<NewsTemplates.PlayoffDrawParameters>(parametersJson)),
            NewsTemplates.AnnouncementPublished => Announcement(
                NewsTemplates.Read<NewsTemplates.AnnouncementParameters>(parametersJson)),
            _ => throw new InvalidOperationException($"'{templateKey}' is not a news template this build renders."),
        };
    }

    private static NewsText Division(NewsTemplates.DivisionParameters parameters) =>
        new(
            $"{parameters.DivisionName} is born",
            $"{parameters.CountryName} has grown: a new tier {parameters.TierNumber} division, "
            + $"{parameters.DivisionName}, is open for business.");

    private static NewsText Transfer(NewsTemplates.TransferParameters parameters) =>
        new(
            $"{parameters.PlayerName} moves",
            $"{parameters.BuyerClubName} has signed {parameters.PlayerName} from {parameters.SellerClubName} "
            + $"for {Money(parameters.FeeMinor)}.");

    private static NewsText FreeAgent(NewsTemplates.FreeAgentParameters parameters) =>
        new(
            $"{parameters.PlayerName} signs",
            $"{parameters.BuyerClubName} has signed {parameters.PlayerName} as a free agent "
            + $"for {Money(parameters.FeeMinor)}.");

    private static NewsText PlayoffDraw(NewsTemplates.PlayoffDrawParameters parameters) =>
        new(
            $"{parameters.UpperDivisionName}: the play-off is drawn",
            $"{parameters.SemiHomeClubName} host {parameters.SemiAwayClubName} in the {parameters.LowerDivisionName} semi-final. "
            + $"The winner plays {parameters.HolderClubName} for a place in {parameters.UpperDivisionName}.");

    private static NewsText Round(NewsTemplates.ResultParameters parameters)
    {
        var score = string.Create(
            CultureInfo.InvariantCulture,
            $"{parameters.HomeGoals}\u2013{parameters.AwayGoals}");

        // The score is the result, so it is not in the headline or the detail: a manager who has not watched
        // this match is not told how it went by the news.
        var heading = parameters.Stage is { } stage
            ? PlayoffStageRules.FromCode(stage).Label()
            : $"Round {parameters.RoundNumber}";

        var shootout = parameters.ShootoutHome is { } home && parameters.ShootoutAway is { } away
            ? string.Create(CultureInfo.InvariantCulture, $" ({home}–{away} on penalties)")
            : string.Empty;

        return new NewsText(
            $"{heading}: {parameters.HomeClubName} v {parameters.AwayClubName}",
            "The result is in.",
            $"{parameters.HomeClubName} {score} {parameters.AwayClubName}{shootout}",
            parameters.MatchId);
    }

    private static string Money(long minorUnits) =>
        minorUnits.ToString("N0", CultureInfo.InvariantCulture);

    private static NewsText Announcement(NewsTemplates.AnnouncementParameters parameters) =>
        new(parameters.Title, parameters.Body);
}

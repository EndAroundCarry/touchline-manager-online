using System.Text.Json;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>A news item that has not been stored yet: its scope, its template, and the facts it names.</summary>
/// <param name="Category">The kind of event the item reports.</param>
/// <param name="TemplateKey">The stable template that renders it.</param>
/// <param name="ParametersJson">The template's parameters, as the document that gets stored.</param>
/// <param name="CountryId">The country the item is scoped to, or null.</param>
/// <param name="DivisionId">The division the item is scoped to, or null.</param>
/// <param name="ExpiresAt">When the item stops being shown, or null for the feed's own retention.</param>
public sealed record NewsDraft(
    NewsCategory Category,
    string TemplateKey,
    string ParametersJson,
    Guid? CountryId,
    Guid? DivisionId,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// The news feed's stable templates and the facts that fill them (`COM-1`, ADR-0029).
/// </summary>
/// <remarks>
/// The counterpart of <see cref="InboxTemplates"/> for the division feed: one file owns every news template so
/// a key is written and read against the same constant, and the writer stores durable tokens that
/// <see cref="NewsMessageText"/> turns back into English (master plan §8.6).
/// </remarks>
public static class NewsTemplates
{
    /// <summary>A new tier joined the pyramid (`PYR-4`).</summary>
    public const string DivisionProvisioned = "news.division.provisioned";

    /// <summary>A transfer completed (`TRF-10`).</summary>
    public const string TransferCompleted = "news.transfer.completed";

    /// <summary>A division's round was published (`MAT-7`).</summary>
    public const string ResultPublished = "news.result.published";

    /// <summary>An operator posted a game notice (`F-46`, ADR-0045).</summary>
    public const string AnnouncementPublished = "news.announcement.published";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the item a newly activated tier produces.</summary>
    /// <param name="countryName">The country whose pyramid grew.</param>
    /// <param name="tierNumber">The tier that was created.</param>
    /// <param name="divisionName">The tier's generated name.</param>
    /// <param name="countryId">The country, which scopes the item.</param>
    /// <param name="divisionId">The division, which scopes the item.</param>
    public static NewsDraft DivisionActivated(
        string countryName,
        int tierNumber,
        string divisionName,
        Guid countryId,
        Guid divisionId) =>
        Write(
            NewsCategory.Division,
            DivisionProvisioned,
            new DivisionParameters(countryName, tierNumber, divisionName),
            countryId,
            divisionId);

    /// <summary>Builds the item a completed transfer produces.</summary>
    /// <param name="playerName">The player who moved.</param>
    /// <param name="feeMinor">The fee, in minor units.</param>
    /// <param name="buyerClubName">The buying club.</param>
    /// <param name="sellerClubName">The selling club.</param>
    public static NewsDraft Transfer(
        string playerName,
        long feeMinor,
        string buyerClubName,
        string sellerClubName) =>
        Write(
            NewsCategory.Transfer,
            TransferCompleted,
            new TransferParameters(playerName, feeMinor, buyerClubName, sellerClubName),
            countryId: null,
            divisionId: null);

    /// <summary>Builds the item a published round produces.</summary>
    /// <param name="roundNumber">The round, 1–34.</param>
    /// <param name="homeClubName">The host.</param>
    /// <param name="homeGoals">The host's goals.</param>
    /// <param name="awayClubName">The visitor.</param>
    /// <param name="awayGoals">The visitor's goals.</param>
    /// <param name="divisionId">The division, which scopes the item.</param>
    /// <param name="matchId">
    /// The played match, so that watching it shows the result here; null for an item that is not tied to one.
    /// </param>
    public static NewsDraft Round(
        int roundNumber,
        string homeClubName,
        int homeGoals,
        string awayClubName,
        int awayGoals,
        Guid divisionId,
        Guid? matchId = null) =>
        Write(
            NewsCategory.Result,
            ResultPublished,
            new ResultParameters(roundNumber, homeClubName, homeGoals, awayClubName, awayGoals, matchId),
            countryId: null,
            divisionId);

    private static NewsDraft Write<TParameters>(
        NewsCategory category,
        string templateKey,
        TParameters parameters,
        Guid? countryId,
        Guid? divisionId,
        DateTimeOffset? expiresAt = null) =>
        new(
            category,
            templateKey,
            JsonSerializer.Serialize(parameters, Options),
            countryId,
            divisionId,
            expiresAt);

    /// <summary>Builds the item an operator's announcement produces (`F-46`, ADR-0045).</summary>
    /// <param name="title">The headline the operator wrote.</param>
    /// <param name="body">The notice the operator wrote.</param>
    /// <param name="countryId">The country to scope it to, or null for the whole world.</param>
    /// <param name="divisionId">The division to scope it to, or null for a broader scope.</param>
    /// <param name="expiresAt">When it stops being shown, or null when it does not expire.</param>
    public static NewsDraft Announcement(
        string title,
        string body,
        Guid? countryId,
        Guid? divisionId,
        DateTimeOffset? expiresAt) =>
        Write(
            NewsCategory.Announcement,
            AnnouncementPublished,
            new AnnouncementParameters(title, body),
            countryId,
            divisionId,
            expiresAt);

    /// <summary>Reads a stored parameter document back.</summary>
    internal static TParameters Read<TParameters>(string parametersJson) =>
        JsonSerializer.Deserialize<TParameters>(parametersJson, Options)
            ?? throw new InvalidOperationException("A stored news item carries no parameters.");

    internal sealed record DivisionParameters(string CountryName, int TierNumber, string DivisionName);

    internal sealed record TransferParameters(
        string PlayerName,
        long FeeMinor,
        string BuyerClubName,
        string SellerClubName);

    internal sealed record ResultParameters(
        int RoundNumber,
        string HomeClubName,
        int HomeGoals,
        string AwayClubName,
        int AwayGoals,
        Guid? MatchId = null);

    internal sealed record AnnouncementParameters(string Title, string Body);
}

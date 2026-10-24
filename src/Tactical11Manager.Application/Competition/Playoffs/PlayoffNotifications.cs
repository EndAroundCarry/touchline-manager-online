using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Application.Competition.Playoffs;

/// <summary>One pair of leagues' bracket as the draw's messages read it.</summary>
/// <param name="WorldId">The world, which scopes the news.</param>
/// <param name="CountryId">The country, which scopes the news.</param>
/// <param name="UpperDivisionId">The higher league, which scopes the news.</param>
/// <param name="UpperDivisionName">The higher league's name.</param>
/// <param name="LowerDivisionName">The lower league's name.</param>
/// <param name="Semi">The semi-final's tie.</param>
/// <param name="SemiFixtureId">The semi-final's match.</param>
/// <param name="SemiKickoffAt">When the semi-final kicks off.</param>
/// <param name="Final">The final's tie, whose away side is still empty.</param>
/// <param name="FinalKickoffAt">When the final is planned to kick off.</param>
public sealed record PlayoffDrawFact(
    Guid WorldId,
    Guid CountryId,
    Guid UpperDivisionId,
    string UpperDivisionName,
    string LowerDivisionName,
    PlayoffTie Semi,
    Guid SemiFixtureId,
    DateTimeOffset SemiKickoffAt,
    PlayoffTie Final,
    DateTimeOffset FinalKickoffAt);

/// <summary>
/// Writes the messages the promotion play-off adds to a matchday's own: the draw, and a final that is set (`PR-4`, ADR-0087).
/// </summary>
/// <remarks>
/// It stages messages and never saves, so they commit with the draw or the publication that produced them, and an AI club, which has no
/// manager, is told nothing (the addressing rule <c>MatchdayNotifications</c> follows).
/// </remarks>
public sealed class PlayoffNotifications
{
    private readonly IInboxRepository _inbox;
    private readonly INewsRepository _news;

    /// <summary>Initializes the composer.</summary>
    public PlayoffNotifications(IInboxRepository inbox, INewsRepository news)
    {
        _inbox = inbox;
        _news = news;
    }

    /// <summary>Tells the managers in a drawn bracket where they stand, and posts the draw to the news.</summary>
    /// <param name="draw">The drawn pair.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task NotifyDrawnAsync(PlayoffDrawFact draw, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draw);

        var semi = draw.Semi;
        var final = draw.Final;

        var targets = (await _inbox.FindClubTargetsAsync(
                [semi.HomeClubId, semi.AwayClubId!.Value, final.HomeClubId],
                cancellationToken))
            .ToDictionary(target => target.ClubId);

        Tell(targets, semi.HomeClubId, PlayoffInboxTemplates.DrawnInto(
            semi.HomePlace,
            PlayoffStage.SemiFinal,
            NameOf(targets, semi.AwayClubId.Value),
            isHome: true,
            draw.SemiKickoffAt,
            draw.SemiFixtureId), now);

        Tell(targets, semi.AwayClubId.Value, PlayoffInboxTemplates.DrawnInto(
            semi.AwayPlace!.Value,
            PlayoffStage.SemiFinal,
            NameOf(targets, semi.HomeClubId),
            isHome: false,
            draw.SemiKickoffAt,
            draw.SemiFixtureId), now);

        // The holder's opponent is not known until the semi-final is played, so its message names none and has no match to link to yet.
        Tell(targets, final.HomeClubId, PlayoffInboxTemplates.DrawnInto(
            final.HomePlace,
            PlayoffStage.Final,
            opponentName: null,
            isHome: true,
            draw.FinalKickoffAt,
            fixtureId: null), now);

        var news = NewsTemplates.PlayoffDraw(
            draw.UpperDivisionName,
            draw.LowerDivisionName,
            NameOf(targets, semi.HomeClubId),
            NameOf(targets, semi.AwayClubId.Value),
            NameOf(targets, final.HomeClubId),
            draw.CountryId,
            draw.UpperDivisionId);

        _news.Add(NewsItem.Publish(
            Guid.CreateVersion7(),
            draw.WorldId,
            news.CountryId,
            news.DivisionId,
            news.Category,
            news.TemplateKey,
            news.ParametersJson,
            publishedAt: now,
            expiresAt: null,
            now));
    }

    /// <summary>Tells the two managers of a final that it is set.</summary>
    /// <param name="final">The final's tie, with both clubs named.</param>
    /// <param name="fixtureId">The final's match.</param>
    /// <param name="kickoffAt">When the final kicks off.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task NotifyFinalSetAsync(
        PlayoffTie final,
        Guid fixtureId,
        DateTimeOffset kickoffAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(final);

        var home = final.HomeClubId;
        var away = final.AwayClubId!.Value;

        var targets = (await _inbox.FindClubTargetsAsync([home, away], cancellationToken))
            .ToDictionary(target => target.ClubId);

        Tell(targets, home, PlayoffInboxTemplates.FinalIsSet(NameOf(targets, away), isHome: true, kickoffAt, fixtureId), now);
        Tell(targets, away, PlayoffInboxTemplates.FinalIsSet(NameOf(targets, home), isHome: false, kickoffAt, fixtureId), now);
    }

    private static string NameOf(Dictionary<Guid, ClubInboxTarget> targets, Guid clubId) =>
        targets.TryGetValue(clubId, out var target) ? target.Name : "A club";

    private void Tell(Dictionary<Guid, ClubInboxTarget> targets, Guid clubId, InboxDraft draft, DateTimeOffset now)
    {
        if (!targets.TryGetValue(clubId, out var target) || target.ManagerId is not { } managerId || managerId == Guid.Empty)
        {
            return;
        }

        _inbox.Add(InboxMessage.Record(
            Guid.CreateVersion7(),
            managerId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            draft.RelatedEntityId,
            now));
    }
}

using Tactical11Manager.Application.Abstractions.Forum;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.Forum;
using Tactical11Manager.Domain.Forum;

namespace Tactical11Manager.Application.Forum;

/// <summary>Reads threads for a given subforum.</summary>
public sealed class GetSubforumThreads
{
    private readonly IForumRepository _forum;
    private readonly IWorldRepository _world;

    public GetSubforumThreads(
        IForumRepository forum,
        IWorldRepository world)
    {
        _forum = forum ?? throw new ArgumentNullException(nameof(forum));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public async Task<ForumThreadListResponse?> ExecuteAsync(
        string subforumId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subforumId);
        var normalizedId = subforumId.Trim().ToLowerInvariant();

        string subforumName;
        string sectionId;
        string sectionTitle;
        string? languageNotice = null;
        bool canCreateThreads = true;
        Guid? countryId = null;

        switch (normalizedId)
        {
            case ForumConstants.SubforumInternationalGeneral:
                subforumName = "General";
                sectionId = ForumConstants.SectionInternational;
                sectionTitle = "International Forum";
                languageNotice = ForumConstants.InternationalLanguageNotice;
                break;

            case ForumConstants.SubforumInternationalDevBugs:
                subforumName = "Development and Bugs";
                sectionId = ForumConstants.SectionInternational;
                sectionTitle = "International Forum";
                languageNotice = ForumConstants.InternationalLanguageNotice;
                break;

            case ForumConstants.SubforumInternationalAnnouncements:
                subforumName = "Announcements";
                sectionId = ForumConstants.SectionInternational;
                sectionTitle = "International Forum";
                languageNotice = ForumConstants.InternationalLanguageNotice;
                canCreateThreads = false;
                break;

            case ForumConstants.SubforumCountryGeneral:
                var context = await _forum.GetManagerContextAsync(managerId, cancellationToken);
                if (context?.CountryId is not { } mgrCountryId)
                {
                    return null;
                }
                countryId = mgrCountryId;
                var country = await _world.FindCountryAsync(mgrCountryId, cancellationToken);
                var countryName = country?.DisplayName ?? "National";

                subforumName = "General";
                sectionId = ForumConstants.SectionCountry;
                sectionTitle = $"{countryName} Forum";
                break;

            default:
                return null;
        }

        var domainThreads = await _forum.GetThreadsBySubforumAsync(normalizedId, countryId, cancellationToken);

        var summaries = domainThreads.Select(t => new ForumThreadSummaryResponse(
            t.Id,
            t.SubforumId,
            t.Title,
            t.AuthorManagerId,
            t.AuthorManagerName,
            t.AuthorClubName,
            t.IsPinned,
            t.IsAnnouncement,
            t.ReplyCount,
            t.CreatedAt,
            t.LastActivityAt,
            t.LatestReplyAuthorName)).ToList();

        return new ForumThreadListResponse(
            normalizedId,
            subforumName,
            sectionId,
            sectionTitle,
            languageNotice,
            canCreateThreads,
            summaries);
    }
}

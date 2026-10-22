using Tactical11Manager.Application.Abstractions.Forum;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.Forum;
using Tactical11Manager.Domain.Forum;

namespace Tactical11Manager.Application.Forum;

/// <summary>Reads thread details and all posts for a forum thread.</summary>
public sealed class GetForumThreadDetail
{
    private readonly IForumRepository _forum;
    private readonly IWorldRepository _world;

    public GetForumThreadDetail(
        IForumRepository forum,
        IWorldRepository world)
    {
        _forum = forum ?? throw new ArgumentNullException(nameof(forum));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public async Task<ForumThreadDetailResponse?> ExecuteAsync(
        Guid threadId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var thread = await _forum.GetThreadByIdAsync(threadId, cancellationToken);
        if (thread == null)
        {
            return null;
        }

        string subforumName;
        string sectionId;
        string sectionTitle;
        string? languageNotice = null;

        if (thread.SubforumId == ForumConstants.SubforumCountryGeneral)
        {
            var context = await _forum.GetManagerContextAsync(managerId, cancellationToken);
            if (context?.CountryId is not { } mgrCountryId || mgrCountryId != thread.CountryId)
            {
                return null;
            }

            var country = await _world.FindCountryAsync(mgrCountryId, cancellationToken);
            var countryName = country?.DisplayName ?? "National";

            subforumName = "General";
            sectionId = ForumConstants.SectionCountry;
            sectionTitle = $"{countryName} Forum";
        }
        else
        {
            sectionId = ForumConstants.SectionInternational;
            sectionTitle = "International Forum";
            languageNotice = ForumConstants.InternationalLanguageNotice;

            subforumName = thread.SubforumId switch
            {
                ForumConstants.SubforumInternationalGeneral => "General",
                ForumConstants.SubforumInternationalDevBugs => "Development and Bugs",
                ForumConstants.SubforumInternationalAnnouncements => "Announcements",
                _ => thread.SubforumId,
            };
        }

        var posts = await _forum.GetPostsByThreadIdAsync(threadId, cancellationToken);
        var postResponses = posts.Select(p => new ForumPostResponse(
            p.Id,
            p.ThreadId,
            p.PostNumber,
            p.AuthorManagerId,
            p.AuthorManagerName,
            p.AuthorClubName,
            p.IsStaff,
            p.Body,
            p.CreatedAt)).ToList();

        return new ForumThreadDetailResponse(
            thread.Id,
            thread.SubforumId,
            subforumName,
            sectionId,
            sectionTitle,
            languageNotice,
            thread.Title,
            thread.AuthorManagerId,
            thread.AuthorManagerName,
            thread.AuthorClubName,
            thread.IsPinned,
            thread.IsAnnouncement,
            CanReply: true,
            thread.CreatedAt,
            postResponses);
    }
}

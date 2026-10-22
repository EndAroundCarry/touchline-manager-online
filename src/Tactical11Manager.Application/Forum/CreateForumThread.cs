using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Forum;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Contracts.Forum;
using Tactical11Manager.Domain.Forum;

namespace Tactical11Manager.Application.Forum;

/// <summary>Result of creating a forum thread.</summary>
public sealed record CreateForumThreadResult(ForumOutcome Outcome, Guid? ThreadId, string? ErrorCode = null);

/// <summary>Creates a new thread in an International or Country subforum.</summary>
public sealed class CreateForumThread
{
    private readonly IForumRepository _forum;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateForumThread(
        IForumRepository forum,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _forum = forum ?? throw new ArgumentNullException(nameof(forum));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<CreateForumThreadResult> ExecuteAsync(
        string subforumId,
        Guid managerId,
        bool isOperator,
        CreateForumThreadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(subforumId))
        {
            return new CreateForumThreadResult(ForumOutcome.SubforumNotFound, null, ForumErrorCodes.SubforumNotFound);
        }

        var normalizedSubforum = subforumId.Trim().ToLowerInvariant();

        if (normalizedSubforum == ForumConstants.SubforumInternationalAnnouncements && !isOperator)
        {
            return new CreateForumThreadResult(ForumOutcome.AnnouncementsRestricted, null, ForumErrorCodes.AnnouncementsRestricted);
        }

        var validSubforums = new[]
        {
            ForumConstants.SubforumInternationalGeneral,
            ForumConstants.SubforumInternationalDevBugs,
            ForumConstants.SubforumInternationalAnnouncements,
            ForumConstants.SubforumCountryGeneral,
        };

        if (!validSubforums.Contains(normalizedSubforum, StringComparer.Ordinal))
        {
            return new CreateForumThreadResult(ForumOutcome.SubforumNotFound, null, ForumErrorCodes.SubforumNotFound);
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length < ForumConstants.TitleMinChars || title.Length > ForumConstants.TitleMaxChars)
        {
            return new CreateForumThreadResult(ForumOutcome.InvalidTitle, null, ForumErrorCodes.TitleLength);
        }

        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length < ForumConstants.ThreadBodyMinChars || body.Length > ForumConstants.BodyMaxChars)
        {
            return new CreateForumThreadResult(ForumOutcome.InvalidBody, null, ForumErrorCodes.BodyLength);
        }

        var managerContext = await _forum.GetManagerContextAsync(managerId, cancellationToken);
        if (managerContext == null)
        {
            return new CreateForumThreadResult(ForumOutcome.ManagerNotFound, null, ForumErrorCodes.ThreadNotFound);
        }

        Guid? countryId = null;
        if (normalizedSubforum == ForumConstants.SubforumCountryGeneral)
        {
            if (managerContext.Value.CountryId == null)
            {
                return new CreateForumThreadResult(ForumOutcome.CountryMismatch, null, ForumErrorCodes.CountryMismatch);
            }
            countryId = managerContext.Value.CountryId;
        }

        var now = _clock.UtcNow;
        var threadId = Guid.CreateVersion7();

        ForumThread thread;
        if (normalizedSubforum == ForumConstants.SubforumInternationalAnnouncements)
        {
            thread = ForumThread.CreateAnnouncement(
                threadId,
                normalizedSubforum,
                countryId: null,
                title,
                managerId,
                managerContext.Value.ManagerName,
                managerContext.Value.ClubName,
                isPinned: true,
                now);
        }
        else
        {
            thread = ForumThread.Create(
                threadId,
                normalizedSubforum,
                countryId,
                title,
                managerId,
                managerContext.Value.ManagerName,
                managerContext.Value.ClubName,
                now);
        }

        var post = ForumPost.Create(
            Guid.CreateVersion7(),
            threadId,
            postNumber: 1,
            managerId,
            managerContext.Value.ManagerName,
            managerContext.Value.ClubName,
            isStaff: isOperator,
            body,
            now);

        _forum.AddThread(thread);
        _forum.AddPost(post);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateForumThreadResult(ForumOutcome.Success, threadId);
    }
}

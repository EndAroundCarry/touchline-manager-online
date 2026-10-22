using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Forum;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Contracts.Forum;
using Tactical11Manager.Domain.Forum;

namespace Tactical11Manager.Application.Forum;

/// <summary>Result of replying to a forum thread.</summary>
public sealed record CreateForumPostResult(ForumOutcome Outcome, ForumPostResponse? Post, string? ErrorCode = null);

/// <summary>Posts a reply to an existing forum thread.</summary>
public sealed class CreateForumPost
{
    private readonly IForumRepository _forum;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateForumPost(
        IForumRepository forum,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _forum = forum ?? throw new ArgumentNullException(nameof(forum));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<CreateForumPostResult> ExecuteAsync(
        Guid threadId,
        Guid managerId,
        bool isOperator,
        CreateForumPostRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var thread = await _forum.GetThreadByIdAsync(threadId, cancellationToken);
        if (thread == null)
        {
            return new CreateForumPostResult(ForumOutcome.ThreadNotFound, null, ForumErrorCodes.ThreadNotFound);
        }

        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length < ForumConstants.ReplyBodyMinChars || body.Length > ForumConstants.BodyMaxChars)
        {
            return new CreateForumPostResult(ForumOutcome.InvalidBody, null, ForumErrorCodes.BodyLength);
        }

        var managerContext = await _forum.GetManagerContextAsync(managerId, cancellationToken);
        if (managerContext == null)
        {
            return new CreateForumPostResult(ForumOutcome.ManagerNotFound, null, ForumErrorCodes.ThreadNotFound);
        }

        if (thread.SubforumId == ForumConstants.SubforumCountryGeneral && thread.CountryId.HasValue)
        {
            if (managerContext.Value.CountryId != thread.CountryId.Value)
            {
                return new CreateForumPostResult(ForumOutcome.CountryMismatch, null, ForumErrorCodes.CountryMismatch);
            }
        }

        var now = _clock.UtcNow;
        var postNumber = thread.ReplyCount + 2;
        var postId = Guid.CreateVersion7();

        var post = ForumPost.Create(
            postId,
            threadId,
            postNumber,
            managerId,
            managerContext.Value.ManagerName,
            managerContext.Value.ClubName,
            isStaff: isOperator,
            body,
            now);

        thread.RecordReply(managerContext.Value.ManagerName, now);

        _forum.AddPost(post);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var response = new ForumPostResponse(
            post.Id,
            post.ThreadId,
            post.PostNumber,
            post.AuthorManagerId,
            post.AuthorManagerName,
            post.AuthorClubName,
            post.IsStaff,
            post.Body,
            post.CreatedAt);

        return new CreateForumPostResult(ForumOutcome.Success, response);
    }
}

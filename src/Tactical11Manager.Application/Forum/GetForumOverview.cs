using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Forum;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.Forum;
using Tactical11Manager.Domain.Forum;

namespace Tactical11Manager.Application.Forum;

/// <summary>Reads the community forum overview visible to the signed-in manager.</summary>
public sealed class GetForumOverview
{
    private readonly IForumRepository _forum;
    private readonly IWorldRepository _world;
    private readonly IClock _clock;

    public GetForumOverview(
        IForumRepository forum,
        IWorldRepository world,
        IClock clock)
    {
        _forum = forum ?? throw new ArgumentNullException(nameof(forum));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ForumOverviewResponse> ExecuteAsync(Guid managerId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // International subforum stats
        var intGenThreads = await _forum.GetThreadCountBySubforumAsync(ForumConstants.SubforumInternationalGeneral, null, cancellationToken);
        var intGenPosts = await _forum.GetPostCountBySubforumAsync(ForumConstants.SubforumInternationalGeneral, null, cancellationToken);
        var intGenActivity = await _forum.GetLatestActivityBySubforumAsync(ForumConstants.SubforumInternationalGeneral, null, cancellationToken);

        var intDevThreads = await _forum.GetThreadCountBySubforumAsync(ForumConstants.SubforumInternationalDevBugs, null, cancellationToken);
        var intDevPosts = await _forum.GetPostCountBySubforumAsync(ForumConstants.SubforumInternationalDevBugs, null, cancellationToken);
        var intDevActivity = await _forum.GetLatestActivityBySubforumAsync(ForumConstants.SubforumInternationalDevBugs, null, cancellationToken);

        var intAnnThreads = await _forum.GetThreadCountBySubforumAsync(ForumConstants.SubforumInternationalAnnouncements, null, cancellationToken);
        var intAnnPosts = await _forum.GetPostCountBySubforumAsync(ForumConstants.SubforumInternationalAnnouncements, null, cancellationToken);
        var intAnnActivity = await _forum.GetLatestActivityBySubforumAsync(ForumConstants.SubforumInternationalAnnouncements, null, cancellationToken);

        var internationalSubforums = new List<ForumSubforumResponse>
        {
            new(
                ForumConstants.SubforumInternationalGeneral,
                ForumConstants.SectionInternational,
                "General",
                "General discussion, tactics, squad building, and football manager chat.",
                CanCreateThreads: true,
                intGenThreads,
                intGenPosts,
                intGenActivity),
            new(
                ForumConstants.SubforumInternationalDevBugs,
                ForumConstants.SectionInternational,
                "Development and Bugs",
                "Feedback on the match engine, bug reports, and suggestions for the game.",
                CanCreateThreads: true,
                intDevThreads,
                intDevPosts,
                intDevActivity),
            new(
                ForumConstants.SubforumInternationalAnnouncements,
                ForumConstants.SectionInternational,
                "Announcements",
                "Official announcements, release notes, maintenance notices, and news.",
                CanCreateThreads: false,
                intAnnThreads,
                intAnnPosts,
                intAnnActivity),
        };

        var internationalSection = new ForumSectionResponse(
            ForumConstants.SectionInternational,
            "International Forum",
            "Global community for tactical discussions, engine development, and official announcements.",
            ForumConstants.InternationalLanguageNotice,
            CountryId: null,
            CountryName: null,
            internationalSubforums);

        var sections = new List<ForumSectionResponse> { internationalSection };

        // Resolve manager's country
        var managerContext = await _forum.GetManagerContextAsync(managerId, cancellationToken);
        if (managerContext?.CountryId is { } countryId)
        {
            var country = await _world.FindCountryAsync(countryId, cancellationToken);
            var countryName = country?.DisplayName ?? "National";

            var countryGenThreads = await _forum.GetThreadCountBySubforumAsync(ForumConstants.SubforumCountryGeneral, countryId, cancellationToken);
            var countryGenPosts = await _forum.GetPostCountBySubforumAsync(ForumConstants.SubforumCountryGeneral, countryId, cancellationToken);
            var countryGenActivity = await _forum.GetLatestActivityBySubforumAsync(ForumConstants.SubforumCountryGeneral, countryId, cancellationToken);

            var countrySubforums = new List<ForumSubforumResponse>
            {
                new(
                    ForumConstants.SubforumCountryGeneral,
                    ForumConstants.SectionCountry,
                    "General",
                    $"Domestic league talk, rivalries, promotions, and manager chat for {countryName}.",
                    CanCreateThreads: true,
                    countryGenThreads,
                    countryGenPosts,
                    countryGenActivity),
            };

            sections.Add(new ForumSectionResponse(
                ForumConstants.SectionCountry,
                $"{countryName} Forum",
                $"Local community forum for managers running clubs in {countryName}.",
                LanguageNotice: null,
                countryId,
                countryName,
                countrySubforums));
        }

        return new ForumOverviewResponse(sections, now);
    }
}

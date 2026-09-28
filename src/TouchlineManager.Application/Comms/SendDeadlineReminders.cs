using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Comms;

/// <summary>What one deadline-reminder pass did.</summary>
/// <param name="MatchdayId">The round the pass was for.</param>
/// <param name="Reminded">How many managers were reminded.</param>
/// <param name="Skipped">Whether the pass was a no-op because the round had already locked or published.</param>
public sealed record SendDeadlineRemindersResult(Guid MatchdayId, int Reminded, bool Skipped);

/// <summary>
/// Writes the deadline reminder for one round, to every club a human holds (`COM-3`, ADR-0029).
/// </summary>
/// <remarks>
/// <para>
/// One message per human club, written in the caller's transaction, plus an email unless the manager has
/// turned reminders off. It is idempotent on its job's business key rather than in the data, so the reminder
/// materialiser enqueues one row per round and the row is what makes it happen once.
/// </para>
/// <para>
/// A round that has already locked or published, or one with no human clubs, is a no-op rather than an error:
/// the caller is a scheduler that may run slightly before or after the boundary it was aiming at.
/// </para>
/// </remarks>
public sealed class SendDeadlineReminders
{
    private readonly IMatchdayRepository _matchdays;
    private readonly IInboxRepository _inbox;
    private readonly INotificationPreferencesRepository _preferences;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public SendDeadlineReminders(
        IMatchdayRepository matchdays,
        IInboxRepository inbox,
        INotificationPreferencesRepository preferences,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _matchdays = matchdays;
        _inbox = inbox;
        _preferences = preferences;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Writes the reminder for one round.</summary>
    /// <param name="matchdayId">The round about to lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SendDeadlineRemindersResult> ExecuteAsync(
        Guid matchdayId,
        CancellationToken cancellationToken)
    {
        var workload = await _matchdays.LoadMatchdayAsync(matchdayId, cancellationToken);

        var now = _clock.UtcNow;

        if (workload is null
            || workload.Matchday.PublicationStatus != MatchdayPublicationStatus.Pending
            || workload.Matchday.LockAt <= now)
        {
            return new SendDeadlineRemindersResult(matchdayId, 0, Skipped: true);
        }

        var clubIds = workload.Fixtures
            .SelectMany(fixture => new[] { fixture.HomeClubId, fixture.AwayClubId })
            .Distinct()
            .ToList();

        if (clubIds.Count == 0)
        {
            return new SendDeadlineRemindersResult(matchdayId, 0, Skipped: true);
        }

        var targets = (await _inbox.FindClubTargetsAsync(clubIds, cancellationToken))
            .ToDictionary(target => target.ClubId);

        var managerIds = targets.Values
            .Where(target => target.ManagerId is { } manager && manager != Guid.Empty)
            .Select(target => target.ManagerId!.Value)
            .Distinct()
            .ToList();

        if (managerIds.Count == 0)
        {
            return new SendDeadlineRemindersResult(matchdayId, 0, Skipped: false);
        }

        var emails = await _inbox.FindManagerEmailsAsync(managerIds, cancellationToken);
        var preferences = await _preferences.FindByManagersAsync(managerIds, cancellationToken);

        var round = workload.Matchday.RoundNumber;
        var lockAt = workload.Matchday.LockAt;
        var reminded = 0;

        foreach (var fixture in workload.Fixtures.OrderBy(fixture => fixture.Id))
        {
            RemindFor(fixture.HomeClubId, fixture.AwayClubId, isHome: true);
            RemindFor(fixture.AwayClubId, fixture.HomeClubId, isHome: false);
        }

        if (reminded > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new SendDeadlineRemindersResult(matchdayId, reminded, Skipped: false);

        void RemindFor(Guid clubId, Guid opponentClubId, bool isHome)
        {
            if (!targets.TryGetValue(clubId, out var target)
                || target.ManagerId is not { } managerId
                || managerId == Guid.Empty)
            {
                return;
            }

            var opponentName = targets.TryGetValue(opponentClubId, out var opponent)
                ? opponent.Name
                : "your opponent";

            var draft = InboxTemplates.Reminder(round, opponentName, isHome, lockAt);

            _inbox.Add(InboxMessage.Record(
                Guid.CreateVersion7(),
                managerId,
                draft.Category,
                draft.TemplateKey,
                draft.ParametersJson,
                draft.RelatedEntityId,
                now));

            var emailAllowed = preferences.TryGetValue(managerId, out var preference)
                ? preference.EmailDeadlineReminders
                : true;

            if (emailAllowed && emails.TryGetValue(managerId, out var email))
            {
                _outbox.Enqueue(EmailOutbox.For(
                    OccupancyEmails.DeadlineReminder(email, target.Name, round, opponentName, lockAt)));
            }

            reminded++;
        }
    }
}

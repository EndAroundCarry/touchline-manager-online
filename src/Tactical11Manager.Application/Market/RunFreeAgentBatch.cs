using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Squad.Generation;

namespace Tactical11Manager.Application.Market;

/// <summary>What one weekly free-agent batch did.</summary>
/// <param name="Created">Whether this call opened the week, as opposed to finding it already open or skipping it.</param>
/// <param name="WeekStart">The Monday the batch is for.</param>
/// <param name="Contracted">The players under contract when the batch was sized.</param>
/// <param name="Generated">The players generated.</param>
/// <param name="Released">The released players added on top.</param>
/// <param name="Gems">The hidden gems among the generated players. Server-only.</param>
public sealed record RunFreeAgentBatchResult(
    bool Created,
    DateOnly WeekStart,
    int Contracted,
    int Generated,
    int Released,
    int Gems);

/// <summary>
/// Opens one week's free-agent pool: generates the players, picks up the released ones, and lists every one on
/// the timed auction (`FA-1`…`FA-6`).
/// </summary>
/// <remarks>
/// <para>
/// The whole week is one unit of work, keyed on the week's Monday: the batch row is unique per world and week,
/// so a retried or duplicated job finds it and does nothing, and a job that failed halfway left nothing
/// behind. The players are a pure function of the world seed and the week (<see cref="FreeAgentGenerator"/>),
/// so a retried batch reproduces the same people.
/// </para>
/// <para>
/// A listing belongs to the batch and has no seller. Every listing closes at the next Monday's opening
/// instant, so the existing auction scheduler resolves last week's listings as this week's open and no
/// cleanup job is needed: a player with a bid is signed there, one without drops off the page.
/// </para>
/// <para>
/// Price and wage come from <see cref="FreeAgentTerms"/> with potential set equal to ability, so nothing
/// visible or priced reveals a hidden gem (`FA-6`).
/// </para>
/// </remarks>
public sealed class RunFreeAgentBatch
{
    private readonly IFreeAgentBatchRepository _batches;
    private readonly IFreeAgentQueries _queries;
    private readonly ISquadRepository _squad;
    private readonly IListingRepository _listings;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;

    /// <summary>Initializes the use case.</summary>
    public RunFreeAgentBatch(
        IFreeAgentBatchRepository batches,
        IFreeAgentQueries queries,
        ISquadRepository squad,
        IListingRepository listings,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        IRequestContext requestContext)
    {
        _batches = batches;
        _queries = queries;
        _squad = squad;
        _listings = listings;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _requestContext = requestContext;
    }

    /// <summary>Opens the batch for a week, or reports that it is already open.</summary>
    /// <param name="weekStart">The Monday the batch opens on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RunFreeAgentBatchResult> ExecuteAsync(DateOnly weekStart, CancellationToken cancellationToken)
    {
        var existing = await _batches.FindByWeekAsync(weekStart, cancellationToken);

        if (existing is not null)
        {
            return new RunFreeAgentBatchResult(
                Created: false,
                weekStart,
                existing.ContractedCount,
                existing.GeneratedCount,
                existing.ReleasedCount,
                existing.GemCount);
        }

        var now = _clock.UtcNow;
        var opensAt = FreeAgentWeeks.OpensAt(weekStart);
        var endsAt = FreeAgentWeeks.EndsAt(weekStart);

        // A week that has already closed (a job that ran more than a week late) would list players whose
        // auction is over before it starts, so it is skipped; the next week's batch picks the released up.
        if (now >= endsAt)
        {
            return Skipped(weekStart);
        }

        var facts = await _queries.GetWorldFactsAsync(cancellationToken);

        if (facts is null)
        {
            return Skipped(weekStart);
        }

        var previous = await _batches.FindPreviousAsync(weekStart, cancellationToken);
        var since = previous?.CreatedAt ?? facts.ReleasedSince;

        var contracted = facts.Countries.Sum(country => country.ContractedPlayers);
        var tierByCountry = facts.Countries.ToDictionary(country => country.CountryCode, country => country.ReferenceTier);

        var generated = FreeAgentGenerator.Generate(
            new FreeAgentBatchRequest(
                facts.WorldSeed,
                facts.WorldId,
                weekStart,
                facts.GameYear,
                [.. facts.Countries.Select(country => new FreeAgentCountry(
                    country.CountryCode,
                    country.NamePoolKey,
                    country.ContractedPlayers))],
                now));

        var released = await _queries.ListReleasedSinceAsync(since, cancellationToken);

        var batch = FreeAgentBatch.Open(
            Guid.CreateVersion7(),
            facts.WorldId,
            weekStart,
            opensAt,
            endsAt,
            contracted,
            generated.Count,
            released.Count,
            generated.Count(player => player.IsGem),
            FreeAgentGenerator.Version,
            now);

        _batches.Add(batch);

        foreach (var agent in generated)
        {
            _squad.AddPlayer(agent.Player);
            _squad.AddPlayerAttributes(agent.Attributes);
            _squad.AddPlayerState(agent.State);

            OpenListing(
                batch,
                agent.Player.Id,
                agent.Ability,
                facts.GameYear - agent.Player.BirthGameYear,
                ReferenceTier(tierByCountry, agent.Player.NationalityCode),
                now);
        }

        foreach (var row in released)
        {
            OpenListing(
                batch,
                row.PlayerId,
                row.Ability,
                row.Age,
                ReferenceTier(tierByCountry, row.NationalityCode),
                now);
        }

        _audit.Record(new AuditEntry(
            MarketAuditActions.FreeAgentBatchOpened,
            AuditActorTypes.Service,
            _requestContext.ActorUserId,
            AuditTargetTypes.FreeAgentBatch,
            batch.Id,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: $"{weekStart:yyyy-MM-dd}: {generated.Count} generated, {released.Count} released"));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RunFreeAgentBatchResult(
            Created: true,
            weekStart,
            contracted,
            batch.GeneratedCount,
            batch.ReleasedCount,
            batch.GemCount);
    }

    private static RunFreeAgentBatchResult Skipped(DateOnly weekStart) =>
        new(Created: false, weekStart, 0, 0, 0, 0);

    /// <summary>A player's reference tier, falling back to the top tier for a nationality with no country.</summary>
    private static int ReferenceTier(Dictionary<string, int> tierByCountry, string nationalityCode) =>
        tierByCountry.GetValueOrDefault(nationalityCode, 1);

    private void OpenListing(
        FreeAgentBatch batch,
        Guid playerId,
        int ability,
        int age,
        int referenceTier,
        DateTimeOffset now)
    {
        var offer = FreeAgentTerms.Calculate(ability, age, referenceTier);

        _listings.Add(TransferListing.OpenFreeAgent(
            Guid.CreateVersion7(),
            playerId,
            batch.Id,
            offer.MinimumFeeMinor,
            offer.WeeklyWageMinor,
            offer.ContractSeasons,
            batch.OpensAt,
            batch.EndsAt,
            now));
    }
}

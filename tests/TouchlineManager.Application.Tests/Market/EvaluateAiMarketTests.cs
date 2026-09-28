using FluentAssertions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Market;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Market;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Tests.Market;

/// <summary>
/// The AI market evaluation: what it decides, how it routes the decisions, and that it commits once
/// (`TRF-12`, `INS-12`).
/// </summary>
/// <remarks>
/// The policy itself is pinned by the domain tests. What this suite proves is the orchestration around it:
/// every decision goes through the shared writers as a service actor, the deterministic daily key is what a
/// retry replays, a refused decision is counted rather than thrown, the decision record names the entity the
/// writer produced, and a pass that decides nothing commits nothing.
/// </remarks>
public sealed class EvaluateAiMarketTests
{
    private static readonly DateOnly Day = new(2026, 9, 25);

    [Fact]
    public async Task Every_decision_is_carried_out_as_a_service_actor_and_committed_once()
    {
        var seller = ClubId(1);
        var buyer = ClubId(2);
        var sellerSquad = FullSquad();
        var buyerSquad = Squad(goalkeepers: 3, defenders: 7, midfielders: 6, attackers: 5);
        var listing = MidfieldListing(Guid.CreateVersion7(), ClubId(9));

        var (useCase, listings, bids, decisions, unitOfWork) = Create(
            [ClubRow(seller, sellerSquad), ClubRow(buyer, buyerSquad)],
            [listing]);

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        result.Clubs.Should().Be(2);
        result.Listed.Should().Be(1, "only the over-target club has a surplus player");
        result.Bids.Should().Be(1, "only the club short of a midfielder has a need");
        result.Skipped.Should().Be(0);

        listings.Calls.Should().ContainSingle();
        listings.Calls[0].Actor.ActorType.Should().Be(AuditActorTypes.Service, "INS-12: the AI is a service actor");
        listings.Calls[0].Actor.UserId.Should().BeNull();
        listings.Calls[0].Key.Should().Be($"ai-l:{seller:N}:{sellerSquad[0].PlayerId:N}:20260925");

        bids.Calls.Should().ContainSingle();
        bids.Calls[0].Actor.ActorType.Should().Be(AuditActorTypes.Service);
        bids.Calls[0].Key.Should().Be($"ai-b:{buyer:N}:{listing.ListingId:N}:20260925");

        decisions.Decisions.Should().HaveCount(2, "one record per decision the writers carried out");
        decisions.Decisions.Should().ContainSingle(decision => decision.Action == AiMarketAction.Listed);
        var bidDecision = decisions.Decisions.Should()
            .ContainSingle(decision => decision.Action == AiMarketAction.Bid).Subject;
        bidDecision.PlayerId.Should().Be(listing.PlayerId, "the decision names the player who was bid for");
        bidDecision.BidId.Should().Be(bids.Calls[0].BidId);

        unitOfWork.Saves.Should().Be(1, "a pass commits once, however many clubs it touched");
    }

    [Fact]
    public async Task A_refused_decision_is_counted_as_skipped_and_records_nothing()
    {
        var (useCase, _, _, decisions, unitOfWork) = Create(
            [ClubRow(ClubId(1), FullSquad())],
            [],
            listingOutcome: MarketOutcome.AlreadyListed);

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        result.Listed.Should().Be(0);
        result.Skipped.Should().Be(1);
        decisions.Decisions.Should().BeEmpty();
        unitOfWork.Saves.Should().Be(0, "nothing was written, so nothing is committed");
    }

    [Fact]
    public async Task A_pass_with_nothing_to_decide_commits_nothing()
    {
        // A full squad at the target with no depleted family: nothing to list and nothing to bid for.
        var squad = Squad(goalkeepers: 3, defenders: 7, midfielders: 7, attackers: 4);

        var (useCase, listings, bids, decisions, unitOfWork) = Create([ClubRow(ClubId(1), squad)], []);

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        result.Listed.Should().Be(0);
        result.Bids.Should().Be(0);
        listings.Calls.Should().BeEmpty();
        bids.Calls.Should().BeEmpty();
        decisions.Decisions.Should().BeEmpty();
        unitOfWork.Saves.Should().Be(0);
    }

    [Fact]
    public async Task The_decision_records_the_inputs_hash_and_policy_version()
    {
        var (useCase, _, _, decisions, _) = Create([ClubRow(ClubId(1), FullSquad())], []);

        await useCase.ExecuteAsync(CancellationToken.None);

        var decision = decisions.Decisions.Should().ContainSingle().Subject;
        decision.PolicyVersion.Should().Be(AiMarketPolicyVersions.Version);
        decision.InputsHash.Should().HaveLength(64, "the digest is a SHA-256 hex string");
        decision.EvaluatedAt.Should().Be(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
    }

    private static (
        EvaluateAiMarket UseCase,
        StubListingWriter Listings,
        StubBidWriter Bids,
        RecordingDecisionRepository Decisions,
        RecordingUnitOfWork UnitOfWork) Create(
            IReadOnlyList<AiMarketClubRow> clubs,
            IReadOnlyList<AiMarketListingRow> listings,
            MarketOutcome listingOutcome = MarketOutcome.Found)
    {
        var listingWriter = new StubListingWriter(listingOutcome);
        var bidWriter = new StubBidWriter();
        var decisions = new RecordingDecisionRepository();
        var unitOfWork = new RecordingUnitOfWork();

        var useCase = new EvaluateAiMarket(
            new StubAiMarketRepository(clubs, listings),
            listingWriter,
            bidWriter,
            decisions,
            unitOfWork,
            new FixedClock(),
            new StubRequestContext());

        return (useCase, listingWriter, bidWriter, decisions, unitOfWork);
    }

    private static AiMarketClubRow ClubRow(Guid clubId, IReadOnlyList<AiMarketPlayerRow> players) =>
        new(clubId, Tier: 1, SpendableMinor: 50_000_000, CurrentSeasonNumber: 1, players);

    private static PositionFamily[] Composition(int goalkeepers, int defenders, int midfielders, int attackers) =>
    [
        .. Enumerable.Repeat(PositionFamily.Goalkeeper, goalkeepers),
        .. Enumerable.Repeat(PositionFamily.Defence, defenders),
        .. Enumerable.Repeat(PositionFamily.Midfield, midfielders),
        .. Enumerable.Repeat(PositionFamily.Attack, attackers),
    ];

    private static List<AiMarketPlayerRow> FullSquad() => Squad(
        WorldRuleSet.GeneratedGoalkeepers,
        WorldRuleSet.GeneratedDefenders,
        WorldRuleSet.GeneratedMidfielders,
        WorldRuleSet.GeneratedAttackers);

    private static List<AiMarketPlayerRow> Squad(int goalkeepers, int defenders, int midfielders, int attackers)
    {
        var families = Composition(goalkeepers, defenders, midfielders, attackers);
        var players = new List<AiMarketPlayerRow>(families.Length);

        for (var index = 0; index < families.Length; index++)
        {
            players.Add(new AiMarketPlayerRow(
                PlayerId(index + 1),
                families[index],
                Ability: 12,
                Potential: 12,
                Age: 25,
                SquadStatus.Rotation,
                ContractEndSeasonNumber: 3,
                IsListed: false));
        }

        return players;
    }

    private static AiMarketListingRow MidfieldListing(Guid listingId, Guid sellerClubId) =>
        new(
            listingId,
            sellerClubId,
            LeadingClubId: null,
            PlayerId(900),
            PositionFamily.Midfield,
            Ability: 12,
            Potential: 12,
            Age: 25,
            MinimumFeeMinor: 1_000_000,
            LeadingAmountMinor: null);

    private static Guid ClubId(int index) => Guid.Parse($"0192f100-0000-7000-8000-{index:D12}");

    private static Guid PlayerId(int ordinal) => Guid.Parse($"0192f300-0000-7000-8000-{ordinal:D12}");

    private sealed class StubAiMarketRepository(
        IReadOnlyList<AiMarketClubRow> clubs,
        IReadOnlyList<AiMarketListingRow> listings) : IAiMarketRepository
    {
        public Task<IReadOnlyList<AiMarketClubRow>> LoadAiClubsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(clubs);

        public Task<IReadOnlyList<AiMarketListingRow>> LoadOpenListingsAsync(
            DateTimeOffset before,
            CancellationToken cancellationToken) =>
            Task.FromResult(listings);
    }

    private sealed class StubListingWriter(MarketOutcome outcome) : IListingWriter
    {
        public List<Call> Calls { get; } = [];

        public Task<ListingWriteResult> ListAsync(
            Guid clubId,
            MarketActor actor,
            Guid playerId,
            long minimumFeeMinor,
            int seasons,
            string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            Calls.Add(new Call(clubId, playerId, minimumFeeMinor, idempotencyKey, actor));

            return Task.FromResult(outcome == MarketOutcome.Found
                ? new ListingWriteResult(MarketOutcome.Found, Guid.CreateVersion7(), Created: true)
                : new ListingWriteResult(outcome, Guid.Empty, Created: false));
        }

        public sealed record Call(
            Guid ClubId,
            Guid PlayerId,
            long MinimumFeeMinor,
            string? Key,
            MarketActor Actor);
    }

    private sealed class StubBidWriter : IBidWriter
    {
        public List<Call> Calls { get; } = [];

        public Task<BidWriteResult> BidAsync(
            Guid clubId,
            MarketActor actor,
            Guid listingId,
            long amountMinor,
            string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            var bidId = Guid.CreateVersion7();
            Calls.Add(new Call(clubId, listingId, amountMinor, idempotencyKey, bidId, actor));

            return Task.FromResult(new BidWriteResult(MarketOutcome.Found, listingId, bidId, Created: true));
        }

        public sealed record Call(
            Guid ClubId,
            Guid ListingId,
            long AmountMinor,
            string? Key,
            Guid BidId,
            MarketActor Actor);
    }

    private sealed class RecordingDecisionRepository : IAiMarketDecisionRepository
    {
        public List<AiMarketDecision> Decisions { get; } = [];

        public void Add(AiMarketDecision decision) => Decisions.Add(decision);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;

            return Task.FromResult(0);
        }

        public Task<IDatabaseTransaction> BeginTransactionAsync(
            TransactionIsolation isolation,
            CancellationToken cancellationToken) =>
            Task.FromResult<IDatabaseTransaction>(new NoOpTransaction());
    }

    private sealed class NoOpTransaction : IDatabaseTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class StubRequestContext : IRequestContext
    {
        public Guid? ActorUserId => null;

        public string CorrelationId { get; } = Guid.CreateVersion7().ToString();

        public string? IpAddress => null;

        public string? UserAgent => null;
    }
}

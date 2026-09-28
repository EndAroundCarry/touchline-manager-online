using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>market.shortlists</c>, a manager's private watch list (`SCT-3`).
/// </summary>
/// <remarks>
/// The uniqueness is per manager and player, which is what makes a re-add an update rather than a duplicate.
/// The note's length is capped in the aggregate and by the column, so a client cannot store an unbounded blob.
/// </remarks>
internal sealed class ShortlistEntryConfiguration : IEntityTypeConfiguration<ShortlistEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShortlistEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("shortlists", "market", table =>
        {
            table.HasCheckConstraint(
                "ck_shortlists_notes_length",
                $"length(notes) <= 280");
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.ManagerId).HasColumnName("manager_id").IsRequired();
        builder.Property(entry => entry.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(entry => entry.Notes).HasColumnName("notes").HasMaxLength(280);
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entry => entry.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(entry => entry.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(entry => new { entry.ManagerId, entry.PlayerId })
            .IsUnique()
            .HasDatabaseName("ux_shortlists_manager_player");
        builder.HasIndex(entry => entry.PlayerId).HasDatabaseName("ix_shortlists_player_id");

        builder.HasOne<Manager>()
            .WithMany()
            .HasForeignKey(entry => entry.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(entry => entry.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>market.transfer_listings</c>, a player offered for sale at a minimum fee (`TRF-1`, `TRF-2`, `TRF-14`).
/// </summary>
internal sealed class TransferListingConfiguration : IEntityTypeConfiguration<TransferListing>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TransferListing> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("transfer_listings", "market", table =>
        {
            table.HasCheckConstraint("ck_transfer_listings_minimum_fee", "minimum_fee_minor > 0");
            table.HasCheckConstraint(
                "ck_transfer_listings_buyer_wage",
                "generated_buyer_wage_minor >= 0");
            table.HasCheckConstraint(
                "ck_transfer_listings_seasons",
                "generated_contract_seasons between 1 and 3");
            table.HasCheckConstraint("ck_transfer_listings_window", "ends_at > opens_at");
        });

        builder.HasKey(listing => listing.Id);
        builder.Property(listing => listing.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(listing => listing.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(listing => listing.SellerClubId).HasColumnName("seller_club_id").IsRequired();
        builder.Property(listing => listing.MinimumFeeMinor).HasColumnName("minimum_fee_minor").IsRequired();
        builder.Property(listing => listing.GeneratedBuyerWageMinor)
            .HasColumnName("generated_buyer_wage_minor")
            .IsRequired();
        builder.Property(listing => listing.GeneratedContractSeasons)
            .HasColumnName("generated_contract_seasons")
            .IsRequired();
        builder.Property(listing => listing.OpensAt).HasColumnName("opens_at").IsRequired();
        builder.Property(listing => listing.EndsAt).HasColumnName("ends_at").IsRequired();
        builder.Property(listing => listing.Status)
            .HasColumnName("status")
            .HasMaxLength(ListingStatuses.MaxCodeLength)
            .HasConversion(status => status.ToCode(), code => ListingStatuses.FromCode(code))
            .IsRequired();
        builder.Property(listing => listing.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(80);
        builder.Property(listing => listing.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(listing => listing.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(listing => listing.Version).HasColumnName("version").IsRequired();

        // One open listing per player (TRF-14).
        builder.HasIndex(listing => listing.PlayerId)
            .IsUnique()
            .HasFilter("status = 'open'")
            .HasDatabaseName("ux_transfer_listings_open_player");

        // The materialiser walks the listings that are open and due (TRF-2).
        builder.HasIndex(listing => new { listing.Status, listing.EndsAt })
            .HasDatabaseName("ix_transfer_listings_status_ends_at");
        builder.HasIndex(listing => listing.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key is not null")
            .HasDatabaseName("ux_transfer_listings_idempotency_key");

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(listing => listing.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(listing => listing.SellerClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>market.transfer_bids</c>, an ascending offer whose leader holds a reservation (`TRF-4`…`TRF-8`).
/// </summary>
internal sealed class TransferBidConfiguration : IEntityTypeConfiguration<TransferBid>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TransferBid> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("transfer_bids", "market", table =>
        {
            table.HasCheckConstraint("ck_transfer_bids_amount", "amount_minor > 0");
            table.HasCheckConstraint("ck_transfer_bids_sequence", "bid_sequence >= 1");
        });

        builder.HasKey(bid => bid.Id);
        builder.Property(bid => bid.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(bid => bid.ListingId).HasColumnName("listing_id").IsRequired();
        builder.Property(bid => bid.BidderClubId).HasColumnName("bidder_club_id").IsRequired();
        builder.Property(bid => bid.AmountMinor).HasColumnName("amount_minor").IsRequired();

        // The database assigns the tie-breaking sequence on insert, so equal amounts resolve to the bid
        // committed first rather than to the arrival time the server observed (TRF-8, T-5).
        builder.Property(bid => bid.BidSequence).HasColumnName("bid_sequence").UseIdentityAlwaysColumn();

        builder.Property(bid => bid.PlacedAt).HasColumnName("placed_at").IsRequired();
        builder.Property(bid => bid.Status)
            .HasColumnName("status")
            .HasMaxLength(BidStatuses.MaxCodeLength)
            .HasConversion(status => status.ToCode(), code => BidStatuses.FromCode(code))
            .IsRequired();
        builder.Property(bid => bid.ReservationCorrelationId)
            .HasColumnName("reservation_correlation_id")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(bid => bid.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(80);
        builder.Property(bid => bid.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(bid => bid.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(bid => bid.Version).HasColumnName("version").IsRequired();

        // One leading bid per listing per club (TRF-6). Bids ascend, so at most one row is ever leading.
        builder.HasIndex(bid => new { bid.ListingId, bid.BidderClubId })
            .IsUnique()
            .HasFilter("status = 'leading'")
            .HasDatabaseName("ux_transfer_bids_leading_listing_club");

        // Resolution reads a listing's bids in winning order (TRF-8).
        builder.HasIndex(bid => new { bid.ListingId, bid.Status, bid.AmountMinor, bid.BidSequence })
            .HasDatabaseName("ix_transfer_bids_listing_status_amount_sequence");
        builder.HasIndex(bid => bid.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key is not null")
            .HasDatabaseName("ux_transfer_bids_idempotency_key");

        builder.HasOne<TransferListing>()
            .WithMany()
            .HasForeignKey(bid => bid.ListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(bid => bid.BidderClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>market.transfer_outcomes</c>, the immutable record of a resolved listing (`TRF-10`).
/// </summary>
internal sealed class TransferOutcomeConfiguration : IEntityTypeConfiguration<TransferOutcome>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TransferOutcome> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("transfer_outcomes", "market", table =>
        {
            table.HasCheckConstraint("ck_transfer_outcomes_fee", "fee_minor > 0");
            table.HasCheckConstraint(
                "ck_transfer_outcomes_distinct_clubs",
                "seller_club_id <> buyer_club_id");
        });

        builder.HasKey(outcome => outcome.Id);
        builder.Property(outcome => outcome.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(outcome => outcome.ListingId).HasColumnName("listing_id").IsRequired();
        builder.Property(outcome => outcome.WinningBidId).HasColumnName("winning_bid_id").IsRequired();
        builder.Property(outcome => outcome.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(outcome => outcome.SellerClubId).HasColumnName("seller_club_id").IsRequired();
        builder.Property(outcome => outcome.BuyerClubId).HasColumnName("buyer_club_id").IsRequired();
        builder.Property(outcome => outcome.FeeMinor).HasColumnName("fee_minor").IsRequired();
        builder.Property(outcome => outcome.OldContractId).HasColumnName("old_contract_id").IsRequired();
        builder.Property(outcome => outcome.NewContractId).HasColumnName("new_contract_id").IsRequired();
        builder.Property(outcome => outcome.OutcomeReason)
            .HasColumnName("outcome_reason")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(outcome => outcome.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(outcome => outcome.ResolvedAt).HasColumnName("resolved_at").IsRequired();

        // Resolution happens once (TRF-9).
        builder.HasIndex(outcome => outcome.ListingId)
            .IsUnique()
            .HasDatabaseName("ux_transfer_outcomes_listing");
        builder.HasIndex(outcome => outcome.ResolvedAt).HasDatabaseName("ix_transfer_outcomes_resolved_at");

        builder.HasOne<TransferListing>()
            .WithMany()
            .HasForeignKey(outcome => outcome.ListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(outcome => outcome.SellerClubId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(outcome => outcome.BuyerClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

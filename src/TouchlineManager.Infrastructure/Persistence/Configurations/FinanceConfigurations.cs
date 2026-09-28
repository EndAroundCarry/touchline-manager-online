using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>finance.club_accounts</c>.
/// </summary>
/// <remarks>
/// The check constraints are the ones `FIN-13` names explicitly: neither balance may go negative, and
/// the reserved amount may never exceed the cash backing it. Expressing them here rather than in the
/// money-moving use cases means no future code path — including an operator repair — can produce a
/// negative balance, because PostgreSQL will refuse the write.
/// </remarks>
internal sealed class ClubAccountConfiguration : IEntityTypeConfiguration<ClubAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_accounts", "finance", table =>
        {
            table.HasCheckConstraint("ck_club_accounts_cash_nonnegative", "cash_minor >= 0");
            table.HasCheckConstraint("ck_club_accounts_reserved_nonnegative", "reserved_minor >= 0");
            table.HasCheckConstraint(
                "ck_club_accounts_reserved_within_cash",
                "reserved_minor <= cash_minor");
            table.HasCheckConstraint("ck_club_accounts_ledger_sequence", "last_ledger_sequence >= 0");
        });

        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(account => account.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(account => account.CashMinor).HasColumnName("cash_minor").IsRequired();
        builder.Property(account => account.ReservedMinor).HasColumnName("reserved_minor").IsRequired();
        builder.Property(account => account.LastLedgerSequence)
            .HasColumnName("last_ledger_sequence")
            .IsRequired();
        builder.Property(account => account.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(account => account.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(account => account.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(account => account.ClubId).IsUnique().HasDatabaseName("ux_club_accounts_club_id");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(account => account.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>finance.ledger_entries</c>, the append-only record a club's balances are the running total of
/// (`FIN-11`, `FIN-12`, `FIN-18`).
/// </summary>
/// <remarks>
/// The constraints are the rules made structural: a sequence starts at one, a category and a source are one
/// of the known codes, a stored resulting balance is never negative and never reserves more than the cash
/// behind it (`FIN-13`), and an entry moves something. The two unique indexes are the guarantee the ledger is
/// written once: the sequence is unique within a club (`FIN-11`), and a correlation key is unique within a
/// category, so an ordinary retry collides with the first entry rather than posting a second (`FIN-17`).
/// </remarks>
internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ledger_entries", "finance", table =>
        {
            table.HasCheckConstraint("ck_ledger_entries_sequence", "sequence >= 1");
            table.HasCheckConstraint(
                "ck_ledger_entries_category",
                "category in ('opening_balance', 'gate_receipt', 'sponsorship', 'wages', 'operating_cost', "
                + "'position_award', 'transfer_payment', 'transfer_proceeds', 'bid_reservation', "
                + "'reservation_release', 'emergency_grant', 'compensation')");
            table.HasCheckConstraint(
                "ck_ledger_entries_source_type",
                "source_type in ('world_seed', 'matchday', 'weekly_run', 'season_rollover', 'transfer', "
                + "'safety_job', 'admin_repair')");
            table.HasCheckConstraint(
                "ck_ledger_entries_balances",
                "resulting_cash_minor >= 0 and resulting_reserved_minor >= 0 "
                + "and resulting_reserved_minor <= resulting_cash_minor");
            table.HasCheckConstraint(
                "ck_ledger_entries_moves",
                "cash_delta_minor <> 0 or reserved_delta_minor <> 0");
            table.HasCheckConstraint("ck_ledger_entries_correlation_id", "length(correlation_id) > 0");
            table.HasCheckConstraint(
                "ck_ledger_entries_description_template",
                "length(description_template) > 0");
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(entry => entry.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(entry => entry.Category)
            .HasColumnName("category")
            .HasMaxLength(LedgerCategories.MaxCodeLength)
            .HasConversion(category => category.ToCode(), code => LedgerCategories.FromCode(code))
            .IsRequired();
        builder.Property(entry => entry.CashDeltaMinor).HasColumnName("cash_delta_minor").IsRequired();
        builder.Property(entry => entry.ReservedDeltaMinor).HasColumnName("reserved_delta_minor").IsRequired();
        builder.Property(entry => entry.ResultingCashMinor).HasColumnName("resulting_cash_minor").IsRequired();
        builder.Property(entry => entry.ResultingReservedMinor)
            .HasColumnName("resulting_reserved_minor")
            .IsRequired();
        builder.Property(entry => entry.SourceType)
            .HasColumnName("source_type")
            .HasMaxLength(LedgerSourceTypes.MaxCodeLength)
            .HasConversion(sourceType => sourceType.ToCode(), code => LedgerSourceTypes.FromCode(code))
            .IsRequired();
        builder.Property(entry => entry.SourceId).HasColumnName("source_id");
        builder.Property(entry => entry.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(LedgerEntry.MaxCorrelationIdLength)
            .IsRequired();
        builder.Property(entry => entry.DescriptionTemplate)
            .HasColumnName("description_template")
            .HasMaxLength(LedgerEntry.MaxDescriptionTemplateLength)
            .IsRequired();
        builder.Property(entry => entry.DescriptionParametersJson)
            .HasColumnName("description_parameters")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();

        // The club's ledger is walked in order, and the sequence is unique within it (FIN-11).
        builder.HasIndex(entry => new { entry.ClubId, entry.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_ledger_entries_club_sequence");

        // Idempotency: one operation, one entry per category (FIN-17).
        builder.HasIndex(entry => new { entry.CorrelationId, entry.Category })
            .IsUnique()
            .HasDatabaseName("ux_ledger_entries_correlation_category");

        // The audit trail follows a source — a fixture, a job, a listing — back to what it moved.
        builder.HasIndex(entry => new { entry.SourceType, entry.SourceId })
            .HasDatabaseName("ix_ledger_entries_source");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(entry => entry.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

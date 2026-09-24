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

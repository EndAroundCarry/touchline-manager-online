using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>finance.club_accounts</c>.
/// </summary>
/// <remarks>
/// The check constraints are the ones `FIN-13` and `DEBT-1` name: the cash may go below zero only as far as the
/// account overdraft floor, reserved funds are never negative, and the reserved amount may never exceed the cash
/// above zero backing it. Expressing them here rather than in the money-moving use cases means no future code
/// path — including an operator repair — can overdraw past the floor, because PostgreSQL will refuse the write.
/// </remarks>
internal sealed class ClubAccountConfiguration : IEntityTypeConfiguration<ClubAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_accounts", "finance", table =>
        {
            table.HasCheckConstraint("ck_club_accounts_cash_floor", "cash_minor >= -overdraft_floor_minor");
            table.HasCheckConstraint("ck_club_accounts_reserved_nonnegative", "reserved_minor >= 0");
            table.HasCheckConstraint(
                "ck_club_accounts_reserved_within_cash",
                "reserved_minor <= greatest(cash_minor, 0)");
            table.HasCheckConstraint("ck_club_accounts_ledger_sequence", "last_ledger_sequence >= 0");
            table.HasCheckConstraint(
                "ck_club_accounts_overdraft",
                "overdraft_limit_minor >= 0 and overdraft_floor_minor >= overdraft_limit_minor");
            table.HasCheckConstraint(
                "ck_club_accounts_financial_state",
                "financial_state in (" + string.Join(", ", FinancialStateCodes.All.Select(code => $"'{code}'")) + ")");
            table.HasCheckConstraint("ck_club_accounts_state_since", "state_since_matchday >= 0");
        });

        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(account => account.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(account => account.CashMinor).HasColumnName("cash_minor").IsRequired();
        builder.Property(account => account.ReservedMinor).HasColumnName("reserved_minor").IsRequired();
        builder.Property(account => account.FinancialState)
            .HasColumnName("financial_state")
            .HasMaxLength(FinancialStateCodes.MaxCodeLength)
            .HasConversion(state => state.ToCode(), code => FinancialStateCodes.FromCode(code))
            .IsRequired();
        builder.Property(account => account.StateSinceMatchday).HasColumnName("state_since_matchday").IsRequired();
        builder.Property(account => account.OverdraftLimitMinor).HasColumnName("overdraft_limit_minor").IsRequired();
        builder.Property(account => account.OverdraftFloorMinor).HasColumnName("overdraft_floor_minor").IsRequired();
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
/// of the known codes, a stored resulting reserve is never negative and never more than the cash above zero
/// behind it (`FIN-13`), and an entry moves something. How far the cash itself may fall is the account floor. The two unique indexes are the guarantee the ledger is
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
                "category in (" + string.Join(", ", LedgerCategories.AllCodes.Select(code => $"'{code}'")) + ")");
            table.HasCheckConstraint(
                "ck_ledger_entries_source_type",
                "source_type in ('world_seed', 'matchday', 'weekly_run', 'season_rollover', 'transfer', "
                + "'safety_job', 'admin_repair', 'stadium', 'contract', 'structure')");
            table.HasCheckConstraint(
                "ck_ledger_entries_balances",
                "resulting_reserved_minor >= 0 and resulting_reserved_minor <= greatest(resulting_cash_minor, 0)");
            table.HasCheckConstraint(
                "ck_ledger_entries_moves",
                "cash_delta_minor <> 0 or reserved_delta_minor <> 0");
            table.HasCheckConstraint("ck_ledger_entries_correlation_id", "length(correlation_id) > 0");
            table.HasCheckConstraint(
                "ck_ledger_entries_description_template",
                "length(description_template) > 0");
            table.HasCheckConstraint(
                "ck_ledger_entries_reverses",
                "reverses_entry_id is null "
                + "or (category = 'compensation' and reverses_entry_id <> id)");
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
        builder.Property(entry => entry.ReversesEntryId).HasColumnName("reverses_entry_id");
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

        // A compensating entry points at the entry it corrects, so the correction is traceable in the ledger
        // itself and the corrected line is never deleted out from under it (FIN-12).
        builder.HasIndex(entry => entry.ReversesEntryId)
            .HasDatabaseName("ix_ledger_entries_reverses_entry_id");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(entry => entry.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerEntry>()
            .WithMany()
            .HasForeignKey(entry => entry.ReversesEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>finance.club_season_finances</c>: one club's opening and closing cash for a season
/// (master plan §6.8).
/// </summary>
/// <remarks>
/// The unique index on <c>(club_id, season_id)</c> is what makes the rollover's summary idempotent: a
/// redelivered settlement cannot write a second summary for a club in the same season.
/// </remarks>
internal sealed class ClubSeasonFinanceConfiguration : IEntityTypeConfiguration<ClubSeasonFinance>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubSeasonFinance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A season can open or close in the red, down to the account overdraft floor, so the cash has no lower bound here (DEBT-1).
        builder.ToTable("club_season_finances", "finance");

        builder.HasKey(summary => summary.Id);
        builder.Property(summary => summary.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(summary => summary.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(summary => summary.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(summary => summary.OpeningCashMinor).HasColumnName("opening_cash_minor").IsRequired();
        builder.Property(summary => summary.ClosingCashMinor).HasColumnName("closing_cash_minor").IsRequired();
        builder.Property(summary => summary.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(summary => summary.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(summary => summary.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(summary => new { summary.ClubId, summary.SeasonId })
            .IsUnique()
            .HasDatabaseName("ux_club_season_finances_club_id_season_id");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(summary => summary.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(summary => summary.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>finance.club_season_finance_lines</c>: one category's total within a season summary
/// (master plan §6.8).
/// </summary>
internal sealed class ClubSeasonFinanceLineConfiguration : IEntityTypeConfiguration<ClubSeasonFinanceLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubSeasonFinanceLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_season_finance_lines", "finance", table => table.HasCheckConstraint(
            "ck_club_season_finance_lines_category",
            "category in (" + string.Join(", ", LedgerCategories.AllCodes.Select(code => $"'{code}'")) + ")"));

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(line => line.ClubSeasonFinanceId).HasColumnName("club_season_finance_id").IsRequired();
        builder.Property(line => line.Category)
            .HasColumnName("category")
            .HasMaxLength(LedgerCategories.MaxCodeLength)
            .HasConversion(category => category.ToCode(), code => LedgerCategories.FromCode(code))
            .IsRequired();
        builder.Property(line => line.CashDeltaMinor).HasColumnName("cash_delta_minor").IsRequired();
        builder.Property(line => line.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(line => line.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(line => line.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(line => new { line.ClubSeasonFinanceId, line.Category })
            .IsUnique()
            .HasDatabaseName("ux_club_season_finance_lines_summary_category");

        builder.HasOne<ClubSeasonFinance>()
            .WithMany()
            .HasForeignKey(line => line.ClubSeasonFinanceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>finance.sponsor_offers</c>: the three offers a sponsor makes a club each season, kept as they were made (`SPN-1`).
/// </summary>
/// <remarks>
/// The unique index on <c>(club_id, season_id, kind)</c> is what makes generating the offers happen once: a retried rollover writes the same
/// three rows and collides with them instead of writing a second set.
/// </remarks>
internal sealed class ClubSponsorOfferConfiguration : IEntityTypeConfiguration<ClubSponsorOffer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubSponsorOffer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sponsor_offers", "finance", table =>
        {
            table.HasCheckConstraint(
                "ck_sponsor_offers_kind",
                "kind in (" + string.Join(", ", SponsorCodes.OfferCodes.Select(code => $"'{code}'")) + ")");
            table.HasCheckConstraint("ck_sponsor_offers_amounts", "fixed_season_minor >= 0 and bonus_multiplier_bp > 0");
            table.HasCheckConstraint("ck_sponsor_offers_basis", "tier >= 1 and fans >= 0 and standing_points between 1 and 100");
            table.HasCheckConstraint("ck_sponsor_offers_name", "length(sponsor_name) > 0");
        });

        builder.HasKey(offer => offer.Id);
        builder.Property(offer => offer.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(offer => offer.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(offer => offer.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(offer => offer.Kind)
            .HasColumnName("kind")
            .HasMaxLength(SponsorCodes.MaxCodeLength)
            .HasConversion(kind => kind.ToCode(), code => SponsorCodes.OfferFromCode(code))
            .IsRequired();
        builder.Property(offer => offer.SponsorName).HasColumnName("sponsor_name").HasMaxLength(60).IsRequired();
        builder.Property(offer => offer.FixedSeasonMinor).HasColumnName("fixed_season_minor").IsRequired();
        builder.Property(offer => offer.BonusMultiplierBp).HasColumnName("bonus_multiplier_bp").IsRequired();
        builder.Property(offer => offer.Tier).HasColumnName("tier").IsRequired();
        builder.Property(offer => offer.Fans).HasColumnName("fans").IsRequired();
        builder.Property(offer => offer.StandingPoints).HasColumnName("standing_points").IsRequired();
        builder.Property(offer => offer.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(offer => new { offer.ClubId, offer.SeasonId, offer.Kind })
            .IsUnique()
            .HasDatabaseName("ux_sponsor_offers_club_season_kind");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(offer => offer.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(offer => offer.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>finance.sponsor_deals</c>: the one deal a club signs for a season, and how the season turned out against its objective (`SPN-2`,
/// `OBJ-1`).
/// </summary>
/// <remarks>
/// The unique index on <c>(club_id, season_id)</c> is what makes a club sign once a season. The shape constraint says a settled deal carries its
/// outcome, its bonus, its place and its time together, and an unsettled one carries none of them.
/// </remarks>
internal sealed class ClubSponsorDealConfiguration : IEntityTypeConfiguration<ClubSponsorDeal>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubSponsorDeal> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sponsor_deals", "finance", table =>
        {
            table.HasCheckConstraint(
                "ck_sponsor_deals_kind",
                "kind in (" + string.Join(", ", SponsorCodes.OfferCodes.Select(code => $"'{code}'")) + ")");
            table.HasCheckConstraint(
                "ck_sponsor_deals_objective",
                "objective in (" + string.Join(", ", SponsorCodes.ObjectiveCodes.Select(code => $"'{code}'")) + ")");
            table.HasCheckConstraint(
                "ck_sponsor_deals_signed_by",
                "signed_by in (" + string.Join(", ", SponsorCodes.SignerCodes.Select(code => $"'{code}'")) + ")");
            table.HasCheckConstraint(
                "ck_sponsor_deals_outcome",
                "outcome is null or outcome in (" + string.Join(", ", SponsorCodes.OutcomeCodes.Select(code => $"'{code}'")) + ")");
            table.HasCheckConstraint("ck_sponsor_deals_target_place", $"target_place between 1 and {WorldRuleSet.LargestDivisionEver}");
            table.HasCheckConstraint(
                "ck_sponsor_deals_amounts",
                "fixed_matchday_minor >= 0 and bonus_met_minor >= 0 and bonus_exceeded_minor >= bonus_met_minor");
            table.HasCheckConstraint(
                "ck_sponsor_deals_settled_shape",
                "(outcome is null and bonus_paid_minor is null and final_place is null and settled_at is null) "
                + "or (outcome is not null and bonus_paid_minor is not null and bonus_paid_minor >= 0 "
                + "and final_place >= 1 and settled_at is not null)");
        });

        builder.HasKey(deal => deal.Id);
        builder.Property(deal => deal.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(deal => deal.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(deal => deal.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(deal => deal.OfferId).HasColumnName("offer_id").IsRequired();
        builder.Property(deal => deal.Kind)
            .HasColumnName("kind")
            .HasMaxLength(SponsorCodes.MaxCodeLength)
            .HasConversion(kind => kind.ToCode(), code => SponsorCodes.OfferFromCode(code))
            .IsRequired();
        builder.Property(deal => deal.Objective)
            .HasColumnName("objective")
            .HasMaxLength(SponsorCodes.MaxCodeLength)
            .HasConversion(objective => objective.ToCode(), code => SponsorCodes.ObjectiveFromCode(code))
            .IsRequired();
        builder.Property(deal => deal.TargetPlace).HasColumnName("target_place").IsRequired();
        builder.Property(deal => deal.FixedMatchdayMinor).HasColumnName("fixed_matchday_minor").IsRequired();
        builder.Property(deal => deal.BonusMetMinor).HasColumnName("bonus_met_minor").IsRequired();
        builder.Property(deal => deal.BonusExceededMinor).HasColumnName("bonus_exceeded_minor").IsRequired();
        builder.Property(deal => deal.SignedBy)
            .HasColumnName("signed_by")
            .HasMaxLength(SponsorCodes.MaxCodeLength)
            .HasConversion(signer => signer.ToCode(), code => SponsorCodes.SignerFromCode(code))
            .IsRequired();
        builder.Property(deal => deal.SignedAt).HasColumnName("signed_at").IsRequired();
        builder.Property(deal => deal.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(SponsorCodes.MaxCodeLength)
            .HasConversion(
                outcome => outcome == null ? null : outcome.Value.ToCode(),
                code => code == null ? null : SponsorCodes.OutcomeFromCode(code));
        builder.Property(deal => deal.BonusPaidMinor).HasColumnName("bonus_paid_minor");
        builder.Property(deal => deal.FinalPlace).HasColumnName("final_place");
        builder.Property(deal => deal.SettledAt).HasColumnName("settled_at");
        builder.Property(deal => deal.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(deal => deal.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

        builder.Ignore(deal => deal.IsSettled);

        builder.HasIndex(deal => new { deal.ClubId, deal.SeasonId })
            .IsUnique()
            .HasDatabaseName("ux_sponsor_deals_club_season");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(deal => deal.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(deal => deal.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ClubSponsorOffer>()
            .WithMany()
            .HasForeignKey(deal => deal.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

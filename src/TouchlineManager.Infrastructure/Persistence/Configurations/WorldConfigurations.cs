using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>world.game_worlds</c>: the single persistent world and the rule set it runs under.
/// </summary>
internal sealed class GameWorldConfiguration : IEntityTypeConfiguration<GameWorld>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GameWorld> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("game_worlds", "world", table =>
        {
            table.HasCheckConstraint(
                "ck_game_worlds_status",
                "status in ('active', 'frozen', 'retired')");
            table.HasCheckConstraint(
                "ck_game_worlds_season_number",
                "current_season_number >= 1");
        });

        builder.HasKey(world => world.Id);
        builder.Property(world => world.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(world => world.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(world => world.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(status => status.ToCode(), code => GameWorldStatuses.FromCode(code))
            .IsRequired();
        builder.Property(world => world.RuleSetVersion)
            .HasColumnName("rule_set_version")
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(world => world.CurrentSeasonNumber).HasColumnName("current_season_number").IsRequired();
        builder.Property(world => world.KickoffUtc).HasColumnName("kickoff_utc").IsRequired();
        builder.Property(world => world.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(world => world.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(world => world.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(world => world.Name).IsUnique().HasDatabaseName("ux_game_worlds_name");
    }
}

/// <summary>
/// Maps <c>world.countries</c>. The unique index on <c>(world_id, code)</c> is the plan's stable
/// country identity; the name-pool key is what makes generated club names reproducible by locale.
/// </summary>
internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("countries", "world", table => table.HasCheckConstraint(
            "ck_countries_code",
            "char_length(code) = 3 and code = upper(code)"));

        builder.HasKey(country => country.Id);
        builder.Property(country => country.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(country => country.WorldId).HasColumnName("world_id").IsRequired();
        builder.Property(country => country.Code).HasColumnName("code").HasMaxLength(3).IsRequired();
        builder.Property(country => country.DisplayName).HasColumnName("display_name").HasMaxLength(60).IsRequired();
        builder.Property(country => country.Locale).HasColumnName("locale").HasMaxLength(16).IsRequired();
        builder.Property(country => country.NamePoolKey)
            .HasColumnName("name_pool_key")
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(country => country.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(country => country.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(country => country.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(country => country.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(country => country.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(country => new { country.WorldId, country.Code })
            .IsUnique()
            .HasDatabaseName("ux_countries_world_id_code");

        builder.HasOne<GameWorld>()
            .WithMany()
            .HasForeignKey(country => country.WorldId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>world.managers</c>. One profile per account, enforced by the unique index on
/// <c>user_id</c> rather than by a convention in the registration use case.
/// </summary>
internal sealed class ManagerConfiguration : IEntityTypeConfiguration<Manager>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Manager> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("managers", "world");

        builder.HasKey(manager => manager.Id);
        builder.Property(manager => manager.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(manager => manager.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(manager => manager.Reputation).HasColumnName("reputation").IsRequired();
        builder.Property(manager => manager.TakeoverCooldownUntil).HasColumnName("takeover_cooldown_until");
        builder.Property(manager => manager.Locale).HasColumnName("locale").HasMaxLength(16).IsRequired();
        builder.Property(manager => manager.TimeZone).HasColumnName("time_zone").HasMaxLength(64).IsRequired();
        builder.Property(manager => manager.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(manager => manager.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(manager => manager.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(manager => manager.UserId)
            .IsUnique()
            .HasDatabaseName("ux_managers_user_id");

        // Restrictive, like every reference to auth.users: deleting an account is a status change, and a
        // cascading delete here would silently remove a manager's competitive history.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(manager => manager.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>world.clubs</c>. Uniqueness is per world for both the comparison name and the slug, which
/// is what stops a generated identity from colliding with one already in the pyramid (`WORLD-3`).
/// </summary>
internal sealed class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("clubs", "world", table =>
        {
            table.HasCheckConstraint("ck_clubs_status", "status in ('active', 'retired')");
            table.HasCheckConstraint("ck_clubs_reputation", "reputation between 1 and 100");
            table.HasCheckConstraint("ck_clubs_stadium_baseline", "stadium_baseline >= 0");
        });

        builder.HasKey(club => club.Id);
        builder.Property(club => club.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(club => club.WorldId).HasColumnName("world_id").IsRequired();
        builder.Property(club => club.CountryId).HasColumnName("country_id").IsRequired();
        builder.Property(club => club.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(club => club.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(club => club.ShortName).HasColumnName("short_name").HasMaxLength(24).IsRequired();
        builder.Property(club => club.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
        builder.Property(club => club.City).HasColumnName("city").HasMaxLength(60).IsRequired();
        builder.Property(club => club.Region).HasColumnName("region").HasMaxLength(60).IsRequired();
        builder.Property(club => club.BadgeSeed).HasColumnName("badge_seed").HasMaxLength(64).IsRequired();
        builder.Property(club => club.FoundingGameYear).HasColumnName("founding_game_year").IsRequired();
        builder.Property(club => club.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(status => status.ToCode(), code => ClubStatuses.FromCode(code))
            .IsRequired();
        builder.Property(club => club.StadiumBaseline).HasColumnName("stadium_baseline").IsRequired();
        builder.Property(club => club.Reputation).HasColumnName("reputation").IsRequired();
        builder.Property(club => club.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(club => club.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(club => club.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(club => new { club.WorldId, club.NormalizedName })
            .IsUnique()
            .HasDatabaseName("ux_clubs_world_id_normalized_name");

        builder.HasIndex(club => new { club.WorldId, club.Slug })
            .IsUnique()
            .HasDatabaseName("ux_clubs_world_id_slug");

        builder.HasIndex(club => club.CountryId).HasDatabaseName("ix_clubs_country_id");

        builder.HasOne<GameWorld>()
            .WithMany()
            .HasForeignKey(club => club.WorldId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Country>()
            .WithMany()
            .HasForeignKey(club => club.CountryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>world.club_tenures</c>, the whole of the ownership model.
/// </summary>
/// <remarks>
/// The two partial unique indexes are the load-bearing constraints of onboarding: one open tenure per
/// club makes a double claim impossible, and one open tenure per manager implements `OCC-9` in the
/// database rather than in a check that a concurrently-running second request could race past.
/// </remarks>
internal sealed class ClubTenureConfiguration : IEntityTypeConfiguration<ClubTenure>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubTenure> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("club_tenures", "world", table =>
        {
            table.HasCheckConstraint(
                "ck_club_tenures_control_status",
                "control_status in ('active', 'inactive', 'closed')");
            table.HasCheckConstraint(
                "ck_club_tenures_end_reason",
                "(ended_at is null and end_reason is null) or (ended_at is not null and end_reason is not null)");
            table.HasCheckConstraint(
                "ck_club_tenures_end_reason_value",
                "end_reason is null or end_reason in ('resigned', 'inactivity_closed', 'administrator_closed')");
        });

        builder.HasKey(tenure => tenure.Id);
        builder.Property(tenure => tenure.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(tenure => tenure.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(tenure => tenure.ManagerId).HasColumnName("manager_id").IsRequired();
        builder.Property(tenure => tenure.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(tenure => tenure.EndedAt).HasColumnName("ended_at");
        builder.Property(tenure => tenure.EndReason).HasColumnName("end_reason").HasMaxLength(32);
        builder.Property(tenure => tenure.LastActiveAt).HasColumnName("last_active_at").IsRequired();
        builder.Property(tenure => tenure.ControlStatus)
            .HasColumnName("control_status")
            .HasMaxLength(16)
            .HasConversion(status => status.ToCode(), code => ClubTenureControlStatusRules.FromCode(code))
            .IsRequired();
        builder.Property(tenure => tenure.TakeoverIdempotencyKey)
            .HasColumnName("takeover_idempotency_key")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(tenure => tenure.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(tenure => tenure.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(tenure => tenure.Version).HasColumnName("version").IsRequired();

        // "Open" spans active and inactive: an inactive tenure still occupies its club, because the
        // manager may return to it (OCC-8).
        builder.HasIndex(tenure => tenure.ClubId)
            .IsUnique()
            .HasFilter("control_status <> 'closed'")
            .HasDatabaseName("ux_club_tenures_open_club");

        builder.HasIndex(tenure => tenure.ManagerId)
            .IsUnique()
            .HasFilter("control_status <> 'closed'")
            .HasDatabaseName("ux_club_tenures_open_manager");

        builder.HasIndex(tenure => tenure.TakeoverIdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_club_tenures_takeover_idempotency_key");

        builder.HasIndex(tenure => new { tenure.ManagerId, tenure.ControlStatus })
            .HasDatabaseName("ix_club_tenures_manager_id_control_status");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(tenure => tenure.ClubId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Manager>()
            .WithMany()
            .HasForeignKey(tenure => tenure.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>world.division_provisioning_requests</c>. The unique index on
/// <c>(country_id, target_tier)</c> is the primary defence against a duplicate tier (`PYR-3`).
/// </summary>
internal sealed class DivisionProvisioningRequestConfiguration : IEntityTypeConfiguration<DivisionProvisioningRequest>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DivisionProvisioningRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("division_provisioning_requests", "world", table =>
        {
            table.HasCheckConstraint(
                "ck_division_provisioning_requests_status",
                "status in ('requested', 'running', 'completed', 'failed')");
            // Tier 1 is seeded, never provisioned (PYR-2).
            table.HasCheckConstraint(
                "ck_division_provisioning_requests_target_tier",
                "target_tier >= 2");
        });

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(request => request.CountryId).HasColumnName("country_id").IsRequired();
        builder.Property(request => request.TargetTier).HasColumnName("target_tier").IsRequired();
        builder.Property(request => request.TargetSeasonId).HasColumnName("target_season_id").IsRequired();
        builder.Property(request => request.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(status => status.ToCode(), code => ProvisioningRequestStatusRules.FromCode(code))
            .IsRequired();
        builder.Property(request => request.GenerationSeed)
            .HasColumnName("generation_seed")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(request => request.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(request => request.StartedAt).HasColumnName("started_at");
        builder.Property(request => request.CompletedAt).HasColumnName("completed_at");
        builder.Property(request => request.FailureDiagnostics)
            .HasColumnName("failure_diagnostics")
            .HasMaxLength(2000);
        builder.Property(request => request.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(request => request.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(request => request.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(request => new { request.CountryId, request.TargetTier })
            .IsUnique()
            .HasDatabaseName("ux_division_provisioning_requests_country_id_target_tier");

        builder.HasOne<Country>()
            .WithMany()
            .HasForeignKey(request => request.CountryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>world.generation_runs</c>. Every generated artifact is traceable to the seed, generator
/// version, and input hash that produced it (`PYR-14`).
/// </summary>
internal sealed class GenerationRunConfiguration : IEntityTypeConfiguration<GenerationRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GenerationRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("generation_runs", "world", table =>
        {
            table.HasCheckConstraint(
                "ck_generation_runs_kind",
                "kind in ('world_bootstrap', 'division_provisioning')");
            table.HasCheckConstraint(
                "ck_generation_runs_status",
                "status in ('running', 'succeeded', 'failed')");
            table.HasCheckConstraint(
                "ck_generation_runs_counts",
                "countries_created >= 0 and clubs_created >= 0 "
                + "and players_created >= 0 and accounts_created >= 0");
        });

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(run => run.Kind)
            .HasColumnName("kind")
            .HasMaxLength(32)
            .HasConversion(kind => kind.ToCode(), code => GenerationRuns.KindFromCode(code))
            .IsRequired();
        builder.Property(run => run.Seed).HasColumnName("seed").HasMaxLength(64).IsRequired();
        builder.Property(run => run.GeneratorVersion)
            .HasColumnName("generator_version")
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(run => run.InputHash).HasColumnName("input_hash").HasMaxLength(64).IsRequired();
        builder.Property(run => run.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(status => status.ToCode(), code => GenerationRuns.StatusFromCode(code))
            .IsRequired();
        builder.Property(run => run.CountriesCreated).HasColumnName("countries_created").IsRequired();
        builder.Property(run => run.ClubsCreated).HasColumnName("clubs_created").IsRequired();
        builder.Property(run => run.PlayersCreated).HasColumnName("players_created").IsRequired();
        builder.Property(run => run.AccountsCreated).HasColumnName("accounts_created").IsRequired();
        builder.Property(run => run.Diagnostics).HasColumnName("diagnostics").HasMaxLength(2000);
        builder.Property(run => run.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(run => run.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(run => new { run.Kind, run.StartedAt }).HasDatabaseName("ix_generation_runs_kind_started_at");
    }
}

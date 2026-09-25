using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>squad.players</c>: the persistent person, including the two server-only hidden values.
/// </summary>
/// <remarks>
/// <c>potential</c> and <c>reputation</c> are class C2 (`data-classification.md` §1). They are ordinary
/// columns on this table rather than a JSONB blob because they are read by the engine and by AI
/// valuation, and they are excluded from every DTO — never serialized to a manager. The check constraint
/// keeps them on the same 1–20 scale as an attribute, which is what lets the development ceiling be
/// compared against current ability without a conversion.
/// </remarks>
internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("players", "squad", table =>
        {
            table.HasCheckConstraint(
                "ck_players_status",
                "status in ('active', 'retired', 'free_agent', 'anonymized')");
            table.HasCheckConstraint(
                "ck_players_preferred_foot",
                "preferred_foot in ('left', 'right', 'both')");
            table.HasCheckConstraint(
                "ck_players_primary_position",
                "primary_position in ('gk', 'rb', 'cb', 'lb', 'dm', 'cm', 'am', 'rw', 'lw', 'st')");
            table.HasCheckConstraint(
                "ck_players_birth_day_of_year",
                "birth_day_of_year between 1 and 366");
            table.HasCheckConstraint(
                "ck_players_physique",
                "height_cm > 0 and weight_kg > 0");
            table.HasCheckConstraint(
                "ck_players_hidden_values_range",
                "potential between 1 and 20 and reputation between 1 and 20");
        });

        builder.HasKey(player => player.Id);
        builder.Property(player => player.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(player => player.WorldId).HasColumnName("world_id").IsRequired();
        builder.Property(player => player.FullName).HasColumnName("full_name").HasMaxLength(80).IsRequired();
        builder.Property(player => player.ShortName).HasColumnName("short_name").HasMaxLength(48).IsRequired();
        builder.Property(player => player.NationalityCode)
            .HasColumnName("nationality_code")
            .HasMaxLength(3)
            .IsRequired();
        builder.Property(player => player.NameSeed).HasColumnName("name_seed").HasMaxLength(64).IsRequired();
        builder.Property(player => player.BirthGameYear).HasColumnName("birth_game_year").IsRequired();
        builder.Property(player => player.BirthDayOfYear).HasColumnName("birth_day_of_year").IsRequired();
        builder.Property(player => player.PreferredFoot)
            .HasColumnName("preferred_foot")
            .HasMaxLength(PreferredFeet.MaxCodeLength)
            .HasConversion(foot => foot.ToCode(), code => PreferredFeet.FromCode(code))
            .IsRequired();
        builder.Property(player => player.HeightCm).HasColumnName("height_cm").HasColumnType("smallint").IsRequired();
        builder.Property(player => player.WeightKg).HasColumnName("weight_kg").HasColumnType("smallint").IsRequired();
        builder.Property(player => player.PrimaryPosition)
            .HasColumnName("primary_position")
            .HasMaxLength(PlayerPositions.MaxCodeLength)
            .HasConversion(position => position.ToCode(), code => PlayerPositions.FromCode(code))
            .IsRequired();
        builder.Property(player => player.SecondaryPositionCodes)
            .HasColumnName("secondary_positions")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(player => player.Status)
            .HasColumnName("status")
            .HasMaxLength(PlayerStatuses.MaxCodeLength)
            .HasConversion(status => status.ToCode(), code => PlayerStatuses.FromCode(code))
            .IsRequired();
        builder.Property(player => player.Potential).HasColumnName("potential").HasColumnType("smallint").IsRequired();
        builder.Property(player => player.Reputation).HasColumnName("reputation").HasColumnType("smallint").IsRequired();
        builder.Property(player => player.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(player => player.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(player => player.Version).HasColumnName("version").IsRequired();

        // Carries the world foreign key and serves the scouting filters that arrive in Stage 10.
        builder.HasIndex(player => new { player.WorldId, player.PrimaryPosition })
            .HasDatabaseName("ix_players_world_position");

        builder.HasOne<GameWorld>()
            .WithMany()
            .HasForeignKey(player => player.WorldId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <c>squad.player_attributes</c>: one row of twenty-eight 1–20 attributes per player.</summary>
/// <remarks>
/// Relational columns rather than JSONB, because scouting filters and the engine's formulas depend on
/// these values individually (`TRN-4`, `JSN-4`). The range checks are grouped by family so a violation
/// names the family that broke rather than only the column.
/// </remarks>
internal sealed class PlayerAttributesConfiguration : IEntityTypeConfiguration<PlayerAttributes>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerAttributes> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("player_attributes", "squad", table =>
        {
            table.HasCheckConstraint(
                "ck_player_attributes_range_technical",
                "finishing between 1 and 20 and passing between 1 and 20 and crossing between 1 and 20 "
                + "and dribbling between 1 and 20 and first_touch between 1 and 20 and tackling between 1 and 20 "
                + "and marking between 1 and 20 and heading between 1 and 20 and technique between 1 and 20 "
                + "and set_pieces between 1 and 20");
            table.HasCheckConstraint(
                "ck_player_attributes_range_mental",
                "decisions between 1 and 20 and vision between 1 and 20 and positioning between 1 and 20 "
                + "and composure between 1 and 20 and anticipation between 1 and 20 and work_rate between 1 and 20 "
                + "and aggression between 1 and 20 and leadership between 1 and 20");
            table.HasCheckConstraint(
                "ck_player_attributes_range_physical",
                "pace between 1 and 20 and acceleration between 1 and 20 and stamina between 1 and 20 "
                + "and strength between 1 and 20 and agility between 1 and 20 and jumping_reach between 1 and 20");
            table.HasCheckConstraint(
                "ck_player_attributes_range_goalkeeping",
                "handling between 1 and 20 and reflexes between 1 and 20 and one_on_ones between 1 and 20 "
                + "and aerial_ability between 1 and 20");
        });

        builder.HasKey(attributes => attributes.PlayerId);
        builder.Property(attributes => attributes.PlayerId).HasColumnName("player_id").ValueGeneratedNever();
        builder.Property(attributes => attributes.Finishing).HasColumnName("finishing").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Passing).HasColumnName("passing").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Crossing).HasColumnName("crossing").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Dribbling).HasColumnName("dribbling").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.FirstTouch).HasColumnName("first_touch").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Tackling).HasColumnName("tackling").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Marking).HasColumnName("marking").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Heading).HasColumnName("heading").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Technique).HasColumnName("technique").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.SetPieces).HasColumnName("set_pieces").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Decisions).HasColumnName("decisions").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Vision).HasColumnName("vision").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Positioning).HasColumnName("positioning").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Composure).HasColumnName("composure").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Anticipation).HasColumnName("anticipation").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.WorkRate).HasColumnName("work_rate").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Aggression).HasColumnName("aggression").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Leadership).HasColumnName("leadership").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Pace).HasColumnName("pace").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Acceleration).HasColumnName("acceleration").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Stamina).HasColumnName("stamina").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Strength).HasColumnName("strength").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Agility).HasColumnName("agility").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.JumpingReach).HasColumnName("jumping_reach").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Handling).HasColumnName("handling").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.Reflexes).HasColumnName("reflexes").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.OneOnOnes).HasColumnName("one_on_ones").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.AerialAbility).HasColumnName("aerial_ability").HasColumnType("smallint").IsRequired();
        builder.Property(attributes => attributes.SchemaVersion).HasColumnName("schema_version").IsRequired();
        builder.Property(attributes => attributes.Checksum).HasColumnName("checksum").HasMaxLength(64).IsRequired();

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(attributes => attributes.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <c>squad.player_state</c>: condition, fatigue, morale, and sharpness in basis points.</summary>
internal sealed class PlayerStateConfiguration : IEntityTypeConfiguration<PlayerState>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("player_state", "squad", table =>
        {
            table.HasCheckConstraint(
                "ck_player_state_basis_points",
                "condition_bp between 0 and 10000 and fatigue_bp between 0 and 10000 "
                + "and morale_bp between 0 and 10000 and match_sharpness_bp between 0 and 10000");
            table.HasCheckConstraint(
                "ck_player_state_development_remainder",
                "development_remainder >= 0");
        });

        builder.HasKey(state => state.PlayerId);
        builder.Property(state => state.PlayerId).HasColumnName("player_id").ValueGeneratedNever();
        builder.Property(state => state.ConditionBp).HasColumnName("condition_bp").IsRequired();
        builder.Property(state => state.FatigueBp).HasColumnName("fatigue_bp").IsRequired();
        builder.Property(state => state.MoraleBp).HasColumnName("morale_bp").IsRequired();
        builder.Property(state => state.MatchSharpnessBp).HasColumnName("match_sharpness_bp").IsRequired();
        builder.Property(state => state.DevelopmentRemainder).HasColumnName("development_remainder").IsRequired();
        builder.Property(state => state.LastProgressionDate).HasColumnName("last_progression_date");
        builder.Property(state => state.Version).HasColumnName("version").IsRequired();

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(state => state.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>squad.player_contracts</c>. The partial unique index is the database half of `SQ-6`: a
/// player has at most one active contract.
/// </summary>
internal sealed class PlayerContractConfiguration : IEntityTypeConfiguration<PlayerContract>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerContract> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("player_contracts", "squad", table =>
        {
            table.HasCheckConstraint("ck_player_contracts_status", "status in ('active', 'closed')");
            table.HasCheckConstraint(
                "ck_player_contracts_squad_status",
                "squad_status in ('key_player', 'first_team', 'rotation', 'prospect')");
            table.HasCheckConstraint(
                "ck_player_contracts_closed_reason",
                "closed_reason is null or closed_reason in ('expired', 'transferred', 'released', 'retired')");
            table.HasCheckConstraint(
                "ck_player_contracts_term",
                "end_season_number >= start_season_number");
            table.HasCheckConstraint("ck_player_contracts_wage", "weekly_wage_minor >= 0");
        });

        builder.HasKey(contract => contract.Id);
        builder.Property(contract => contract.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(contract => contract.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(contract => contract.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(contract => contract.StartSeasonNumber).HasColumnName("start_season_number").IsRequired();
        builder.Property(contract => contract.EndSeasonNumber).HasColumnName("end_season_number").IsRequired();
        builder.Property(contract => contract.WeeklyWageMinor).HasColumnName("weekly_wage_minor").IsRequired();
        builder.Property(contract => contract.SquadStatus)
            .HasColumnName("squad_status")
            .HasMaxLength(ContractCodes.MaxSquadStatusCodeLength)
            .HasConversion(status => status.ToCode(), code => ContractCodes.SquadStatusFromCode(code))
            .IsRequired();
        builder.Property(contract => contract.Status)
            .HasColumnName("status")
            .HasMaxLength(ContractCodes.MaxStatusCodeLength)
            .HasConversion(status => status.ToCode(), code => ContractCodes.StatusFromCode(code))
            .IsRequired();
        builder.Property(contract => contract.ClosedReason).HasColumnName("closed_reason").HasMaxLength(16);
        builder.Property(contract => contract.ClosedAt).HasColumnName("closed_at");
        builder.Property(contract => contract.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(contract => contract.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(contract => contract.Version).HasColumnName("version").IsRequired();

        // One index on player_id, and it is the filtered one: the only hot lookup is "the player's active
        // contract", and the field it filters on is exactly the "active" the query needs. A player row is
        // never deleted, so nothing else needs a full index on this column.
        builder.HasIndex(contract => contract.PlayerId)
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("ux_player_contracts_active_player");
        builder.HasIndex(contract => new { contract.ClubId, contract.EndSeasonNumber })
            .HasDatabaseName("ix_player_contracts_club_end_season");

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(contract => contract.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(contract => contract.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>squad.player_registrations</c>. The partial unique index is the database half of `SQ-6`: a
/// player has at most one active registration.
/// </summary>
internal sealed class PlayerRegistrationConfiguration : IEntityTypeConfiguration<PlayerRegistration>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerRegistration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("player_registrations", "squad", table =>
        {
            table.HasCheckConstraint("ck_player_registrations_status", "status in ('active', 'ended')");
            table.HasCheckConstraint(
                "ck_player_registrations_boundary",
                "effective_fixture_boundary_round >= 0");
        });

        builder.HasKey(registration => registration.Id);
        builder.Property(registration => registration.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(registration => registration.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(registration => registration.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(registration => registration.EffectiveSeasonId)
            .HasColumnName("effective_season_id")
            .IsRequired();
        builder.Property(registration => registration.EffectiveFixtureBoundaryRound)
            .HasColumnName("effective_fixture_boundary_round")
            .IsRequired();
        builder.Property(registration => registration.Status)
            .HasColumnName("status")
            .HasMaxLength(RegistrationStatuses.MaxCodeLength)
            .HasConversion(status => status.ToCode(), code => RegistrationStatuses.FromCode(code))
            .IsRequired();
        builder.Property(registration => registration.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(registration => registration.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(registration => registration.Version).HasColumnName("version").IsRequired();

        // As on contracts, the filtered index on player_id is the one that serves the eligibility lookup,
        // and it is the only one on the column.
        builder.HasIndex(registration => registration.PlayerId)
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("ux_player_registrations_active_player");
        builder.HasIndex(registration => registration.ClubId)
            .HasDatabaseName("ix_player_registrations_club_id");
        builder.HasIndex(registration => registration.EffectiveSeasonId)
            .HasDatabaseName("ix_player_registrations_season_id");

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(registration => registration.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(registration => registration.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(registration => registration.EffectiveSeasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>squad.player_unavailability</c>: absences measured in eligible fixtures, not in days
/// (`TRN-12`, `DIS-5`).
/// </summary>
/// <remarks>
/// <c>source_fixture_id</c> carries its foreign key to <c>competition.fixtures</c> now that the fixture
/// table exists. It is nullable, because an absence can also come from training rather than from a match
/// (`TRN-12`), so a check constraint and not a not-null is what keeps the reference honest.
/// </remarks>
internal sealed class PlayerUnavailabilityConfiguration : IEntityTypeConfiguration<PlayerUnavailability>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerUnavailability> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("player_unavailability", "squad", table =>
        {
            table.HasCheckConstraint(
                "ck_player_unavailability_type",
                "type in ('injury', 'suspension')");
            table.HasCheckConstraint(
                "ck_player_unavailability_severity",
                "severity in ('minor', 'moderate', 'major')");
            table.HasCheckConstraint(
                "ck_player_unavailability_remaining",
                "remaining_fixtures >= 0");
        });

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(record => record.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(record => record.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(record => record.Type)
            .HasColumnName("type")
            .HasMaxLength(Unavailabilities.MaxTypeCodeLength)
            .HasConversion(type => type.ToCode(), code => Unavailabilities.TypeFromCode(code))
            .IsRequired();
        builder.Property(record => record.SourceFixtureId).HasColumnName("source_fixture_id");
        builder.Property(record => record.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(record => record.RemainingFixtures).HasColumnName("remaining_fixtures").IsRequired();
        builder.Property(record => record.Severity)
            .HasColumnName("severity")
            .HasMaxLength(Unavailabilities.MaxSeverityCodeLength)
            .HasConversion(severity => severity.ToCode(), code => Unavailabilities.SeverityFromCode(code))
            .IsRequired();
        builder.Property(record => record.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(record => record.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(record => record.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(record => record.Version).HasColumnName("version").IsRequired();

        // Selection asks "who is unavailable right now", which is the open-record filter. The club index
        // serves the squad availability view.
        builder.HasIndex(record => record.PlayerId)
            .HasFilter("resolved_at is null")
            .HasDatabaseName("ix_player_unavailability_open_player");
        builder.HasIndex(record => record.ClubId).HasDatabaseName("ix_player_unavailability_club_id");

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(record => record.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(record => record.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Fixture>()
            .WithMany()
            .HasForeignKey(record => record.SourceFixtureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>squad.tactical_plans</c>. The partial unique index is `INS-11`: a club has exactly one
/// default plan.
/// </summary>
internal sealed class TacticalPlanConfiguration : IEntityTypeConfiguration<TacticalPlan>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TacticalPlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tactical_plans", "squad", table =>
        {
            table.HasCheckConstraint(
                "ck_tactical_plans_formation",
                "formation_preset in ('4-4-2', '4-3-3', '4-2-3-1', '4-1-4-1', '3-5-2', '5-3-2')");
            table.HasCheckConstraint(
                "ck_tactical_plans_mentality",
                "mentality in ('defensive', 'cautious', 'balanced', 'positive', 'attacking')");
            table.HasCheckConstraint("ck_tactical_plans_tempo", "tempo in ('low', 'normal', 'high')");
            table.HasCheckConstraint("ck_tactical_plans_passing", "passing in ('short', 'mixed', 'direct')");
            table.HasCheckConstraint("ck_tactical_plans_width", "width in ('narrow', 'normal', 'wide')");
            table.HasCheckConstraint(
                "ck_tactical_plans_pressing",
                "pressing in ('low_block', 'mid_block', 'high_press')");
            table.HasCheckConstraint("ck_tactical_plans_defensive_line", "defensive_line in ('deep', 'normal', 'high')");
            table.HasCheckConstraint(
                "ck_tactical_plans_tackling",
                "tackling in ('stay_on_feet', 'normal', 'aggressive')");
            table.HasCheckConstraint(
                "ck_tactical_plans_time_wasting",
                "time_wasting in ('off', 'situational', 'on')");
        });

        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(plan => plan.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(plan => plan.Name).HasColumnName("name").HasMaxLength(TacticalPlan.MaxNameLength).IsRequired();
        builder.Property(plan => plan.FormationPreset)
            .HasColumnName("formation_preset")
            .HasMaxLength(FormationPresets.MaxCodeLength)
            .HasConversion(preset => preset.ToCode(), code => FormationPresets.FromCode(code))
            .IsRequired();
        builder.Property(plan => plan.Mentality)
            .HasColumnName("mentality")
            .HasMaxLength(TeamInstructions.MaxCodeLength)
            .HasConversion(value => value.ToCode(), code => TeamInstructions.MentalityFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.Tempo)
            .HasColumnName("tempo")
            .HasMaxLength(TeamInstructions.MaxCodeLength)
            .HasConversion(value => value.ToCode(), code => TeamInstructions.TempoFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.Passing)
            .HasColumnName("passing")
            .HasMaxLength(TeamInstructions.MaxCodeLength)
            .HasConversion(value => value.ToCode(), code => TeamInstructions.PassingFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.Width)
            .HasColumnName("width")
            .HasMaxLength(TeamInstructions.MaxCodeLength)
            .HasConversion(value => value.ToCode(), code => TeamInstructions.WidthFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.Pressing)
            .HasColumnName("pressing")
            .HasMaxLength(TeamInstructions.MaxCodeLength)
            .HasConversion(value => value.ToCode(), code => TeamInstructions.PressingFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.DefensiveLine)
            .HasColumnName("defensive_line")
            .HasMaxLength(TeamInstructions.MaxCodeLength)
            .HasConversion(value => value.ToCode(), code => TeamInstructions.LineFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.Tackling)
            .HasColumnName("tackling")
            .HasMaxLength(TeamInstructions.MaxCodeLength)
            .HasConversion(value => value.ToCode(), code => TeamInstructions.TacklingFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.TimeWasting)
            .HasColumnName("time_wasting")
            .HasMaxLength(TeamInstructions.MaxCodeLength)
            .HasConversion(value => value.ToCode(), code => TeamInstructions.TimeWastingFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.IsDefault).HasColumnName("is_default").IsRequired();
        builder.Property(plan => plan.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(plan => plan.UpdatedAt).HasColumnName("updated_at").IsRequired();
        // The plan's version is the strong entity tag a manager's save carries back in If-Match, so it is
        // also the concurrency token: a save that raced another is refused with 0 rows affected rather
        // than silently overwriting the winner (CONC-1, ADR-0009).
        builder.Property(plan => plan.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

        // The default-plan lookup is the one that matters and the filter is exactly its predicate, so the
        // filtered index is the only one on club_id.
        builder.HasIndex(plan => plan.ClubId)
            .IsUnique()
            .HasFilter("is_default")
            .HasDatabaseName("ux_tactical_plans_default_club");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(plan => plan.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>squad.tactical_slots</c>. Slot and assigned-player uniqueness and the 0–10,000 coordinate
/// checks are what make a saved layout a valid pitch (`TAC-7`, `TAC-9`).
/// </summary>
internal sealed class TacticalSlotConfiguration : IEntityTypeConfiguration<TacticalSlot>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TacticalSlot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tactical_slots", "squad", table =>
        {
            table.HasCheckConstraint("ck_tactical_slots_number", "slot_number between 1 and 11");
            table.HasCheckConstraint(
                "ck_tactical_slots_coordinates",
                "normalized_x between 0 and 10000 and normalized_y between 0 and 10000");
            table.HasCheckConstraint(
                "ck_tactical_slots_position_family",
                "position_family in ('goalkeeper', 'defence', 'midfield', 'attack')");
            table.HasCheckConstraint(
                "ck_tactical_slots_role",
                "role in ('goalkeeper', 'centre_back', 'full_back', 'wing_back', 'defensive_midfielder', "
                + "'central_midfielder', 'attacking_midfielder', 'winger', 'striker')");
        });

        builder.HasKey(slot => slot.Id);
        builder.Property(slot => slot.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(slot => slot.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(slot => slot.SlotNumber).HasColumnName("slot_number").HasColumnType("smallint").IsRequired();
        builder.Property(slot => slot.PositionFamily)
            .HasColumnName("position_family")
            .HasMaxLength(PositionFamilies.MaxCodeLength)
            .HasConversion(family => family.ToCode(), code => PositionFamilies.FromCode(code))
            .IsRequired();
        builder.Property(slot => slot.Role)
            .HasColumnName("role")
            .HasMaxLength(PlayerRoles.MaxCodeLength)
            .HasConversion(role => role.ToCode(), code => PlayerRoles.FromCode(code))
            .IsRequired();
        builder.Property(slot => slot.NormalizedX).HasColumnName("normalized_x").IsRequired();
        builder.Property(slot => slot.NormalizedY).HasColumnName("normalized_y").IsRequired();
        builder.Property(slot => slot.AssignedPlayerId).HasColumnName("assigned_player_id");
        builder.Property(slot => slot.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(slot => slot.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(slot => slot.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(slot => new { slot.PlanId, slot.SlotNumber })
            .IsUnique()
            .HasDatabaseName("ux_tactical_slots_plan_slot");
        builder.HasIndex(slot => new { slot.PlanId, slot.AssignedPlayerId })
            .IsUnique()
            .HasFilter("assigned_player_id is not null")
            .HasDatabaseName("ux_tactical_slots_plan_player");
        builder.HasIndex(slot => slot.AssignedPlayerId).HasDatabaseName("ix_tactical_slots_assigned_player_id");

        builder.HasOne<TacticalPlan>()
            .WithMany()
            .HasForeignKey(slot => slot.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(slot => slot.AssignedPlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <c>squad.training_plans</c>: one current training plan per club.</summary>
internal sealed class TrainingPlanConfiguration : IEntityTypeConfiguration<TrainingPlan>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TrainingPlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("training_plans", "squad", table =>
        {
            table.HasCheckConstraint(
                "ck_training_plans_focus",
                "team_focus in ('balanced', 'recovery', 'fitness', 'attacking', 'defending', 'technical', 'tactical')");
            table.HasCheckConstraint(
                "ck_training_plans_intensity",
                "intensity in ('light', 'normal', 'intense')");
        });

        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(plan => plan.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(plan => plan.TeamFocus)
            .HasColumnName("team_focus")
            .HasMaxLength(TrainingPlans.MaxFocusCodeLength)
            .HasConversion(focus => focus.ToCode(), code => TrainingPlans.FromCode(code))
            .IsRequired();
        builder.Property(plan => plan.Intensity)
            .HasColumnName("intensity")
            .HasMaxLength(TrainingPlans.MaxIntensityCodeLength)
            .HasConversion(intensity => intensity.ToCode(), code => TrainingPlans.IntensityFromCode(code))
            .IsRequired();
        builder.Property(plan => plan.EffectiveDate).HasColumnName("effective_date").IsRequired();
        builder.Property(plan => plan.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(plan => plan.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(plan => plan.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(plan => plan.ClubId).IsUnique().HasDatabaseName("ux_training_plans_club");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(plan => plan.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <c>squad.player_training_focus</c>: the optional per-player focus (`TRN-2`).</summary>
internal sealed class PlayerTrainingFocusConfiguration : IEntityTypeConfiguration<PlayerTrainingFocus>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerTrainingFocus> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("player_training_focus", "squad", table => table.HasCheckConstraint(
            "ck_player_training_focus_family",
            "focus_family in ('technical', 'mental', 'physical', 'goalkeeping')"));

        builder.HasKey(focus => focus.Id);
        builder.Property(focus => focus.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(focus => focus.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(focus => focus.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(focus => focus.FocusFamily)
            .HasColumnName("focus_family")
            .HasMaxLength(AttributeFamilies.MaxCodeLength)
            .HasConversion(family => family.ToCode(), code => AttributeFamilies.FromCode(code))
            .IsRequired();
        builder.Property(focus => focus.EffectiveDate).HasColumnName("effective_date").IsRequired();
        builder.Property(focus => focus.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(focus => focus.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(focus => focus.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(focus => focus.PlayerId).IsUnique().HasDatabaseName("ux_player_training_focus_player");
        builder.HasIndex(focus => focus.ClubId).HasDatabaseName("ix_player_training_focus_club_id");

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(focus => focus.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(focus => focus.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>squad.fixture_team_sheets</c>: a club's selection for one fixture.
/// </summary>
/// <remarks>
/// <c>fixture_id</c> carries its foreign key now that <c>competition.fixtures</c> exists, and the sheet's
/// <c>version</c> is a concurrency token, because it is the strong entity tag a prepared side is saved
/// against — a save that raced another is refused by the database and not only by the use case's own
/// comparison (`CONC-1`, ADR-0009).
/// </remarks>
internal sealed class FixtureTeamSheetConfiguration : IEntityTypeConfiguration<FixtureTeamSheet>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FixtureTeamSheet> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fixture_team_sheets", "squad", table =>
        {
            table.HasCheckConstraint("ck_fixture_team_sheets_status", "status in ('draft', 'locked')");
            table.HasCheckConstraint(
                "ck_fixture_team_sheets_plan_version",
                "tactical_plan_version > 0");
        });

        builder.HasKey(sheet => sheet.Id);
        builder.Property(sheet => sheet.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(sheet => sheet.FixtureId).HasColumnName("fixture_id").IsRequired();
        builder.Property(sheet => sheet.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(sheet => sheet.TacticalPlanId).HasColumnName("tactical_plan_id").IsRequired();
        builder.Property(sheet => sheet.TacticalPlanVersion)
            .HasColumnName("tactical_plan_version")
            .IsRequired();
        builder.Property(sheet => sheet.Status)
            .HasColumnName("status")
            .HasMaxLength(TeamSheetStatuses.MaxCodeLength)
            .HasConversion(status => status.ToCode(), code => TeamSheetStatuses.FromCode(code))
            .IsRequired();
        builder.Property(sheet => sheet.LockedAt).HasColumnName("locked_at");
        builder.Property(sheet => sheet.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(sheet => sheet.UpdatedAt).HasColumnName("updated_at").IsRequired();
        // The sheet's version is the strong entity tag a prepared side is saved against, so it is also the
        // concurrency token: a save that raced another is refused with 0 rows affected rather than silently
        // overwriting the winner (CONC-1, ADR-0009).
        builder.Property(sheet => sheet.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

        builder.HasIndex(sheet => new { sheet.FixtureId, sheet.ClubId })
            .IsUnique()
            .HasDatabaseName("ux_fixture_team_sheets_fixture_club");
        builder.HasIndex(sheet => sheet.ClubId).HasDatabaseName("ix_fixture_team_sheets_club_id");
        builder.HasIndex(sheet => sheet.TacticalPlanId).HasDatabaseName("ix_fixture_team_sheets_tactical_plan_id");

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(sheet => sheet.ClubId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Fixture>()
            .WithMany()
            .HasForeignKey(sheet => sheet.FixtureId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TacticalPlan>()
            .WithMany()
            .HasForeignKey(sheet => sheet.TacticalPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>squad.team_sheet_entries</c>. The sheet is one selection for one fixture, so its entries are
/// deleted with it if a draft is discarded; the database keeps the restrictive default like everywhere
/// else and the application decides.
/// </summary>
internal sealed class TeamSheetEntryConfiguration : IEntityTypeConfiguration<TeamSheetEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TeamSheetEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("team_sheet_entries", "squad", table =>
        {
            table.HasCheckConstraint(
                "ck_team_sheet_entries_designation",
                "designation in ('starter', 'substitute')");
            table.HasCheckConstraint(
                "ck_team_sheet_entries_slot",
                $"slot_number between {TeamSheetEntry.FirstSlotNumber} and {TeamSheetEntry.LastSlotNumber}");
            table.HasCheckConstraint(
                "ck_team_sheet_entries_role_override",
                "role_override is null or role_override in ('goalkeeper', 'centre_back', 'full_back', "
                + "'wing_back', 'defensive_midfielder', 'central_midfielder', 'attacking_midfielder', "
                + "'winger', 'striker')");
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.TeamSheetId).HasColumnName("team_sheet_id").IsRequired();
        builder.Property(entry => entry.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(entry => entry.Designation)
            .HasColumnName("designation")
            .HasMaxLength(TeamSheetDesignations.MaxCodeLength)
            .HasConversion(designation => designation.ToCode(), code => TeamSheetDesignations.FromCode(code))
            .IsRequired();
        builder.Property(entry => entry.SlotNumber).HasColumnName("slot_number").HasColumnType("smallint").IsRequired();
        builder.Property(entry => entry.RoleOverride)
            .HasColumnName("role_override")
            .HasMaxLength(PlayerRoles.MaxCodeLength)
            .HasConversion(role => role!.Value.ToCode(), code => PlayerRoles.FromCode(code));
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entry => entry.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(entry => entry.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(entry => new { entry.TeamSheetId, entry.PlayerId })
            .IsUnique()
            .HasDatabaseName("ux_team_sheet_entries_sheet_player");
        builder.HasIndex(entry => new { entry.TeamSheetId, entry.SlotNumber })
            .IsUnique()
            .HasDatabaseName("ux_team_sheet_entries_sheet_slot");
        builder.HasIndex(entry => entry.PlayerId).HasDatabaseName("ix_team_sheet_entries_player_id");

        builder.HasOne<FixtureTeamSheet>()
            .WithMany()
            .HasForeignKey(entry => entry.TeamSheetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(entry => entry.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

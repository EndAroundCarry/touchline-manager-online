using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// The match module's mapping: frozen inputs, results, events, and attempts (master plan §6.6).
/// </summary>
/// <remarks>
/// <para>
/// The database carries the guarantees the workflow depends on, so a defect cannot become a wrong result:
/// one snapshot per fixture and one match per fixture are unique constraints rather than promises, an event
/// sequence is unique within its match, and an attempt number is unique within its fixture. Retries are the
/// normal case here (§7.1, ADR-0003), and each of those constraints turns a double-insert into a refusal
/// rather than a second row.
/// </para>
/// <para>
/// The two JSONB columns are the ones master plan §4.5 permits: an immutable versioned match input snapshot
/// and a typed match payload. Both documents carry a schema discriminator, both are validated by the
/// application on the way in, and neither is queried — anything the world filters on is a column.
/// </para>
/// </remarks>
internal sealed class InputSnapshotConfiguration : IEntityTypeConfiguration<InputSnapshot>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InputSnapshot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("input_snapshots", "match", table =>
        {
            table.HasCheckConstraint("ck_input_snapshots_snapshot_hash", "length(snapshot_hash) > 0");
            table.HasCheckConstraint("ck_input_snapshots_seed_commitment", "length(seed_commitment) > 0");
            table.HasCheckConstraint("ck_input_snapshots_engine_version", "length(engine_version) > 0");
        });

        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(snapshot => snapshot.FixtureId).HasColumnName("fixture_id").IsRequired();
        builder.Property(snapshot => snapshot.EngineVersion).HasColumnName("engine_version").HasMaxLength(32).IsRequired();
        builder.Property(snapshot => snapshot.RuleSetVersion).HasColumnName("rule_set_version").HasMaxLength(32).IsRequired();

        // The seed is the derivation material, never serialized to a manager (MAT-11). PostgreSQL has no
        // unsigned 64-bit integer, so it is stored as a numeric wide enough to hold every value.
        builder.Property(snapshot => snapshot.Seed).HasColumnName("seed").HasColumnType("numeric(20,0)").IsRequired();

        builder.Property(snapshot => snapshot.SeedCommitment).HasColumnName("seed_commitment").HasMaxLength(64).IsRequired();
        builder.Property(snapshot => snapshot.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb").IsRequired();
        builder.Property(snapshot => snapshot.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64).IsRequired();
        builder.Property(snapshot => snapshot.CreatedAt).HasColumnName("created_at").IsRequired();

        // A fixture is frozen once. This is what makes a retried lock job harmless rather than a second
        // snapshot a simulator might pick up.
        builder.HasIndex(snapshot => snapshot.FixtureId)
            .IsUnique()
            .HasDatabaseName("ux_input_snapshots_fixture_id");

        builder.HasOne<Fixture>().WithMany()
            .HasForeignKey(snapshot => snapshot.FixtureId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps a simulated result (master plan §6.6).</summary>
internal sealed class SimulatedMatchConfiguration : IEntityTypeConfiguration<SimulatedMatch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SimulatedMatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("matches", "match", table =>
        {
            table.HasCheckConstraint("ck_matches_score_non_negative", "home_goals >= 0 and away_goals >= 0");
            table.HasCheckConstraint("ck_matches_completed_after_started", "completed_at >= started_at");
            table.HasCheckConstraint("ck_matches_output_hash", "length(output_hash) > 0");
        });

        builder.HasKey(match => match.Id);
        builder.Property(match => match.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(match => match.FixtureId).HasColumnName("fixture_id").IsRequired();
        builder.Property(match => match.EngineVersion).HasColumnName("engine_version").HasMaxLength(32).IsRequired();
        builder.Property(match => match.RuleSetVersion).HasColumnName("rule_set_version").HasMaxLength(32).IsRequired();
        builder.Property(match => match.SeedCommitment).HasColumnName("seed_commitment").HasMaxLength(64).IsRequired();
        builder.Property(match => match.HomeGoals).HasColumnName("home_goals").IsRequired();
        builder.Property(match => match.AwayGoals).HasColumnName("away_goals").IsRequired();
        builder.Property(match => match.StatisticsJson).HasColumnName("statistics").HasColumnType("jsonb").IsRequired();
        builder.Property(match => match.InputHash).HasColumnName("input_hash").HasMaxLength(64).IsRequired();
        builder.Property(match => match.OutputHash).HasColumnName("output_hash").HasMaxLength(64).IsRequired();
        builder.Property(match => match.SimulationAttemptId).HasColumnName("simulation_attempt_id").IsRequired();
        builder.Property(match => match.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(match => match.CompletedAt).HasColumnName("completed_at").IsRequired();

        // A fixture is simulated once. A retried resolution finds the staged fixture and does nothing; if it
        // somehow did not, this is what stops a second result existing for one match (MAT-9).
        builder.HasIndex(match => match.FixtureId)
            .IsUnique()
            .HasDatabaseName("ux_matches_fixture_id");

        builder.HasIndex(match => match.OutputHash).HasDatabaseName("ix_matches_output_hash");

        builder.HasOne<Fixture>().WithMany()
            .HasForeignKey(match => match.FixtureId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SimulationAttempt>().WithMany()
            .HasForeignKey(match => match.SimulationAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps one stored match event (master plan §6.6).</summary>
internal sealed class MatchEventConfiguration : IEntityTypeConfiguration<MatchEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MatchEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var types = string.Join(
            ", ",
            MatchEventTypes.All.Select(type => $"'{type.ToCode()}'"));

        builder.ToTable("events", "match", table =>
        {
            table.HasCheckConstraint("ck_events_sequence", "sequence >= 1");
            table.HasCheckConstraint("ck_events_minute", "minute >= 0 and stoppage_minute >= 0");
            table.HasCheckConstraint("ck_events_type", $"event_type in ({types})");
            table.HasCheckConstraint(
                "ck_events_quality",
                "quality_basis_points is null or (quality_basis_points between 0 and 10000)");
            table.HasCheckConstraint(
                "ck_events_absence",
                "absence_fixtures is null or absence_fixtures >= 0");
        });

        builder.HasKey(matchEvent => matchEvent.Id);
        builder.Property(matchEvent => matchEvent.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(matchEvent => matchEvent.MatchId).HasColumnName("match_id").IsRequired();
        builder.Property(matchEvent => matchEvent.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(matchEvent => matchEvent.Minute).HasColumnName("minute").IsRequired();
        builder.Property(matchEvent => matchEvent.StoppageMinute).HasColumnName("stoppage_minute").IsRequired();
        builder.Property(matchEvent => matchEvent.ClubId).HasColumnName("club_id").IsRequired();
        builder.Property(matchEvent => matchEvent.Type)
            .HasColumnName("event_type")
            .HasMaxLength(MatchEventTypes.MaxCodeLength)
            .HasConversion(type => type.ToCode(), code => MatchEventTypes.FromCode(code))
            .IsRequired();
        builder.Property(matchEvent => matchEvent.ParticipantId).HasColumnName("participant_id");
        builder.Property(matchEvent => matchEvent.SecondaryParticipantId).HasColumnName("secondary_participant_id");

        builder.Property(matchEvent => matchEvent.Zone)
            .HasColumnName("zone")
            .HasMaxLength(12)
            .HasConversion(
                zone => zone == null ? null : zone.Value.ToCode(),
                code => code == null ? null : MatchEventTypes.ZoneFromCode(code));

        builder.Property(matchEvent => matchEvent.QualityBasisPoints).HasColumnName("quality_basis_points");
        builder.Property(matchEvent => matchEvent.AbsenceFixtures).HasColumnName("absence_fixtures");

        builder.Property(matchEvent => matchEvent.SubstitutionCause)
            .HasColumnName("substitution_cause")
            .HasMaxLength(10)
            .HasConversion(
                cause => cause == null ? null : cause.Value.ToCode(),
                code => code == null ? null : MatchEventTypes.CauseFromCode(code));

        // The sequence is a total order within a match, so it is unique: two events cannot claim the same
        // position, which is what lets a viewer read the match back in the order it happened.
        builder.HasIndex(matchEvent => new { matchEvent.MatchId, matchEvent.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_events_match_id_sequence");

        builder.HasIndex(matchEvent => new { matchEvent.MatchId, matchEvent.Minute })
            .HasDatabaseName("ix_events_match_id_minute");

        // What a discipline or statistics projection reads: the events of one type for one club.
        builder.HasIndex(matchEvent => new { matchEvent.ClubId, matchEvent.Type })
            .HasDatabaseName("ix_events_club_id_event_type");

        builder.HasOne<SimulatedMatch>().WithMany()
            .HasForeignKey(matchEvent => matchEvent.MatchId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Club>().WithMany()
            .HasForeignKey(matchEvent => matchEvent.ClubId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps one simulation attempt (master plan §6.6).</summary>
internal sealed class SimulationAttemptConfiguration : IEntityTypeConfiguration<SimulationAttempt>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SimulationAttempt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("simulation_attempts", "match", table =>
        {
            table.HasCheckConstraint("ck_simulation_attempts_number", "attempt_number >= 1");
            table.HasCheckConstraint("ck_simulation_attempts_status", "status in ('succeeded', 'failed')");
            table.HasCheckConstraint("ck_simulation_attempts_completed", "completed_at >= started_at");
            table.HasCheckConstraint(
                "ck_simulation_attempts_result",
                "(status = 'succeeded' and input_hash is not null and output_hash is not null) "
                + "or (status = 'failed' and error_message is not null)");
        });

        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(attempt => attempt.FixtureId).HasColumnName("fixture_id").IsRequired();
        builder.Property(attempt => attempt.JobId).HasColumnName("job_id");
        builder.Property(attempt => attempt.AttemptNumber).HasColumnName("attempt_number").IsRequired();
        builder.Property(attempt => attempt.EngineVersion).HasColumnName("engine_version").HasMaxLength(32).IsRequired();
        builder.Property(attempt => attempt.Status)
            .HasColumnName("status")
            .HasMaxLength(SimulationAttemptStatuses.MaxCodeLength)
            .HasConversion(status => status.ToCode(), code => SimulationAttemptStatuses.FromCode(code))
            .IsRequired();
        builder.Property(attempt => attempt.InputHash).HasColumnName("input_hash").HasMaxLength(64);
        builder.Property(attempt => attempt.OutputHash).HasColumnName("output_hash").HasMaxLength(64);
        builder.Property(attempt => attempt.ErrorCategory).HasColumnName("error_category").HasMaxLength(64);
        builder.Property(attempt => attempt.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        builder.Property(attempt => attempt.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(attempt => attempt.CompletedAt).HasColumnName("completed_at").IsRequired();
        builder.Property(attempt => attempt.DurationMilliseconds).HasColumnName("duration_ms").IsRequired();

        // An attempt number happens once per fixture, so "attempt 2" always means the same try and a
        // duplicated attempt cannot be mistaken for a second one.
        builder.HasIndex(attempt => new { attempt.FixtureId, attempt.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("ux_simulation_attempts_fixture_id_attempt_number");

        builder.HasOne<Fixture>().WithMany()
            .HasForeignKey(attempt => attempt.FixtureId).OnDelete(DeleteBehavior.Restrict);
    }
}

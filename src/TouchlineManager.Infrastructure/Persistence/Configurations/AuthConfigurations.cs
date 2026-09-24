using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>auth.refresh_sessions</c>. The unique index on the token hash is the lookup path for a
/// refresh, and the family index is what lets reuse detection revoke a whole family in one statement.
/// </summary>
internal sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_sessions", "auth", table =>
        {
            table.HasCheckConstraint("ck_refresh_sessions_expiry", "expires_at > issued_at");
            table.HasCheckConstraint(
                "ck_refresh_sessions_revocation",
                "(revoked_at is null and revocation_reason is null) "
                + "or (revoked_at is not null and revocation_reason is not null)");
        });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(session => session.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(session => session.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(session => session.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(session => session.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(session => session.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(session => session.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(session => session.RevokedAt).HasColumnName("revoked_at");
        builder.Property(session => session.RevocationReason)
            .HasColumnName("revocation_reason")
            .HasMaxLength(32);
        builder.Property(session => session.ReplacedBySessionId).HasColumnName("replaced_by_session_id");
        builder.Property(session => session.IpPrefixHash).HasColumnName("ip_prefix_hash").HasMaxLength(64);
        builder.Property(session => session.UserAgentHash).HasColumnName("user_agent_hash").HasMaxLength(64);
        builder.Property(session => session.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(session => session.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_sessions_token_hash");

        builder.HasIndex(session => session.FamilyId).HasDatabaseName("ix_refresh_sessions_family_id");

        builder.HasIndex(session => new { session.UserId, session.RevokedAt })
            .HasDatabaseName("ix_refresh_sessions_user_id_revoked_at");

        // Restrictive, not cascading: deleting an account is a status change, never a delete, so a
        // cascading delete here would be a path to silently losing session history.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>auth.email_tokens</c>. The unique index on the token hash is the lookup path; the
/// user/purpose index is what makes "supersede every older link" a single statement.
/// </summary>
internal sealed class EmailTokenConfiguration : IEntityTypeConfiguration<EmailToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("email_tokens", "auth", table => table.HasCheckConstraint(
            "ck_email_tokens_expiry",
            "expires_at > created_at"));

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(token => token.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(token => token.Purpose)
            .HasColumnName("purpose")
            .HasMaxLength(32)
            .HasConversion(purpose => purpose.ToStorageValue(), code => EmailTokenPurposes.FromStorageValue(code))
            .IsRequired();
        builder.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(token => token.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(token => token.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(token => token.ConsumedAt).HasColumnName("consumed_at");

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_email_tokens_token_hash");

        builder.HasIndex(token => new { token.UserId, token.Purpose })
            .HasDatabaseName("ix_email_tokens_user_id_purpose");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>auth.user_consents</c>. Append-only evidence of which document version was accepted when
/// (master plan §12.4).
/// </summary>
internal sealed class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserConsent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_consents", "auth", table => table.HasCheckConstraint(
            "ck_user_consents_document_type",
            "document_type in ('terms', 'privacy')"));

        builder.HasKey(consent => consent.Id);
        builder.Property(consent => consent.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(consent => consent.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(consent => consent.DocumentType)
            .HasColumnName("document_type")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(consent => consent.Version).HasColumnName("version").HasMaxLength(32).IsRequired();
        builder.Property(consent => consent.AcceptedAt).HasColumnName("accepted_at").IsRequired();
        builder.Property(consent => consent.IpHash).HasColumnName("ip_hash").HasMaxLength(64);

        builder.HasIndex(consent => new { consent.UserId, consent.DocumentType })
            .HasDatabaseName("ix_user_consents_user_id_document_type");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(consent => consent.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>ops.audit_log</c>. Append-only and indexed for operator investigation by target, actor,
/// and correlation ID.
/// </summary>
internal sealed class OpsAuditEntryConfiguration : IEntityTypeConfiguration<Entities.OpsAuditEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Entities.OpsAuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_log", "ops", table => table.HasCheckConstraint(
            "ck_audit_log_actor_type",
            "actor_type in ('user', 'service', 'anonymous')"));

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.ActorType).HasColumnName("actor_type").HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(entry => entry.Action).HasColumnName("action").HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.TargetType).HasColumnName("target_type").HasMaxLength(60);
        builder.Property(entry => entry.TargetId).HasColumnName("target_id");
        builder.Property(entry => entry.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(entry => entry.IpHash).HasColumnName("ip_hash").HasMaxLength(64);
        builder.Property(entry => entry.BeforeMetadata).HasColumnName("before_metadata").HasColumnType("jsonb");
        builder.Property(entry => entry.AfterMetadata).HasColumnName("after_metadata").HasColumnType("jsonb");
        builder.Property(entry => entry.Reason).HasColumnName("reason").HasMaxLength(200);

        builder.HasIndex(entry => new { entry.TargetType, entry.TargetId, entry.OccurredAt })
            .HasDatabaseName("ix_audit_log_target_occurred_at");

        builder.HasIndex(entry => new { entry.ActorUserId, entry.OccurredAt })
            .HasDatabaseName("ix_audit_log_actor_user_id_occurred_at");

        builder.HasIndex(entry => entry.CorrelationId).HasDatabaseName("ix_audit_log_correlation_id");
    }
}

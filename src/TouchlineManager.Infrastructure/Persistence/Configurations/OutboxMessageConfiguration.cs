using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>ops.outbox_messages</c>: the dispatch intents written in a domain transaction (master plan §6.9,
/// `MOD-4`).
/// </summary>
/// <remarks>
/// The dispatcher's read is "unpublished rows that are due", so it has an index on
/// <c>(status, due_at)</c> rather than a scan of the table. The payload is JSONB because it is a versioned
/// dispatch document; the correlation id is a column because a retried operation is matched against it.
/// </remarks>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var statuses = string.Join(
            ", ",
            Enum.GetValues<OutboxMessageStatus>().Select(status => $"'{status.ToCode()}'"));

        builder.ToTable("outbox_messages", "ops", table =>
        {
            table.HasCheckConstraint("ck_outbox_messages_status", $"status in ({statuses})");
            table.HasCheckConstraint("ck_outbox_messages_type", "length(type) > 0");
            table.HasCheckConstraint("ck_outbox_messages_attempts", "attempt_count >= 0 and max_attempts >= 1");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(message => message.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
        builder.Property(message => message.AggregateType)
            .HasColumnName("aggregate_type")
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(message => message.AggregateId).HasColumnName("aggregate_id");
        builder.Property(message => message.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(message => message.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(message => message.DueAt).HasColumnName("due_at").IsRequired();
        builder.Property(message => message.Status)
            .HasColumnName("status")
            .HasMaxLength(OutboxMessageStatuses.MaxCodeLength)
            .HasConversion(status => status.ToCode(), code => OutboxMessageStatuses.FromCode(code))
            .IsRequired();
        builder.Property(message => message.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(message => message.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(message => message.PublishedAt).HasColumnName("published_at");
        builder.Property(message => message.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(message => message.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(message => message.Version).HasColumnName("version").IsRequired();

        // The dispatcher's read: unpublished rows that are due, oldest first.
        builder.HasIndex(message => new { message.Status, message.DueAt })
            .HasDatabaseName("ix_outbox_messages_status_due_at");

        // A retried operation is matched on its correlation key.
        builder.HasIndex(message => message.CorrelationId).HasDatabaseName("ix_outbox_messages_correlation_id");
    }
}

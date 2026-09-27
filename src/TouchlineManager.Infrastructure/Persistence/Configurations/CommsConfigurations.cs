using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// The comms module's mapping: the messages a manager reads (master plan §6.9).
/// </summary>
/// <remarks>
/// <para>
/// The category is a check-constrained code rather than an ordinal, so a stored message's shelf is the code
/// the domain names and a reordered enum cannot silently move messages between shelves. The parameters are
/// JSONB because §4.5 permits exactly this: a versioned, application-validated document whose members are
/// never filtered on — anything a query needs is a column.
/// </para>
/// <para>
/// The unread count is the one read that runs on every poll, so it has a partial index on the unread rows
/// rather than scanning a manager's whole history. The reader walks the list newest-first, so the same
/// partial shape is mirrored on the paging index.
/// </para>
/// </remarks>
internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var categories = string.Join(
            ", ",
            InboxCategories.All.Select(category => $"'{category.ToCode()}'"));

        builder.ToTable("inbox_messages", "comms", table =>
        {
            table.HasCheckConstraint("ck_inbox_messages_category", $"category in ({categories})");
            table.HasCheckConstraint("ck_inbox_messages_template_key", "length(template_key) > 0");
            table.HasCheckConstraint("ck_inbox_messages_version", "version >= 1");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(message => message.RecipientManagerId).HasColumnName("recipient_manager_id").IsRequired();
        builder.Property(message => message.Category)
            .HasColumnName("category")
            .HasMaxLength(InboxCategories.MaxCodeLength)
            .HasConversion(category => category.ToCode(), code => InboxCategories.FromCode(code))
            .IsRequired();
        builder.Property(message => message.TemplateKey)
            .HasColumnName("template_key")
            .HasMaxLength(InboxMessage.MaxTemplateKeyLength)
            .IsRequired();
        builder.Property(message => message.ParametersJson).HasColumnName("parameters").HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.RelatedEntityId).HasColumnName("related_entity_id");
        builder.Property(message => message.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(message => message.ReadAt).HasColumnName("read_at");
        builder.Property(message => message.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(message => message.Version).HasColumnName("version").IsRequired();

        // The badge's read: every unread message of one manager, and nothing else.
        builder.HasIndex(message => message.RecipientManagerId)
            .HasDatabaseName("ix_inbox_messages_recipient_manager_id_unread")
            .HasFilter("read_at is null");

        // The list's read: one manager's messages newest-first, which is the keyset order.
        builder.HasIndex(message => new { message.RecipientManagerId, message.CreatedAt, message.Id })
            .HasDatabaseName("ix_inbox_messages_recipient_manager_id_created_at_id");

        // A message is addressed to a manager and outlives the club it was about, so the recipient is a
        // restrictive reference rather than a cascade: deleting an account does not silently delete its mail.
        builder.HasOne<Manager>().WithMany()
            .HasForeignKey(message => message.RecipientManagerId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Competition;
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

/// <summary>
/// Maps <c>comms.news_items</c>: the public feed a division or country shows (master plan §6.9, Stage 11).
/// </summary>
/// <remarks>
/// The feed is read newest-first within a scope, so the paging indexes are keyed on the scope and the
/// publication instant. The parameters are JSONB for the same reason the inbox's are: a versioned document
/// whose members are never filtered on.
/// </remarks>
internal sealed class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NewsItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var categories = string.Join(", ", NewsCategories.All.Select(category => $"'{category.ToCode()}'"));

        builder.ToTable("news_items", "comms", table =>
        {
            table.HasCheckConstraint("ck_news_items_category", $"category in ({categories})");
            table.HasCheckConstraint("ck_news_items_template_key", "length(template_key) > 0");
            table.HasCheckConstraint("ck_news_items_version", "version >= 1");
            table.HasCheckConstraint(
                "ck_news_items_expiry",
                "expires_at is null or expires_at > published_at");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.WorldId).HasColumnName("world_id").IsRequired();
        builder.Property(item => item.CountryId).HasColumnName("country_id");
        builder.Property(item => item.DivisionId).HasColumnName("division_id");
        builder.Property(item => item.Category)
            .HasColumnName("category")
            .HasMaxLength(NewsCategories.MaxCodeLength)
            .HasConversion(category => category.ToCode(), code => NewsCategories.FromCode(code))
            .IsRequired();
        builder.Property(item => item.TemplateKey).HasColumnName("template_key").HasMaxLength(64).IsRequired();
        builder.Property(item => item.ParametersJson).HasColumnName("parameters").HasColumnType("jsonb").IsRequired();
        builder.Property(item => item.PublishedAt).HasColumnName("published_at").IsRequired();
        builder.Property(item => item.ExpiresAt).HasColumnName("expires_at");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(item => item.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(item => new { item.DivisionId, item.PublishedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_news_items_division_id_published_at");

        builder.HasIndex(item => new { item.CountryId, item.PublishedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_news_items_country_id_published_at");

        builder.HasIndex(item => item.PublishedAt).HasDatabaseName("ix_news_items_published_at");

        builder.HasOne<GameWorld>().WithMany()
            .HasForeignKey(item => item.WorldId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Country>().WithMany()
            .HasForeignKey(item => item.CountryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Division>().WithMany()
            .HasForeignKey(item => item.DivisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>comms.notification_preferences</c>: which notification emails a manager receives (Stage 11).
/// </summary>
/// <remarks>
/// One row per manager, enforced by a unique index. A missing row means the defaults, so a manager who never
/// changes a preference costs no write.
/// </remarks>
internal sealed class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationPreferences> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_preferences", "comms", table =>
            table.HasCheckConstraint("ck_notification_preferences_version", "version >= 1"));

        builder.HasKey(preferences => preferences.Id);
        builder.Property(preferences => preferences.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(preferences => preferences.ManagerId).HasColumnName("manager_id").IsRequired();
        builder.Property(preferences => preferences.EmailDeadlineReminders)
            .HasColumnName("email_deadline_reminders").IsRequired();
        builder.Property(preferences => preferences.EmailInactivityWarnings)
            .HasColumnName("email_inactivity_warnings").IsRequired();
        builder.Property(preferences => preferences.EmailMarketMessages)
            .HasColumnName("email_market_messages").IsRequired();
        builder.Property(preferences => preferences.EmailNewsDigest)
            .HasColumnName("email_news_digest").IsRequired();
        builder.Property(preferences => preferences.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(preferences => preferences.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(preferences => preferences.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(preferences => preferences.ManagerId)
            .IsUnique()
            .HasDatabaseName("ux_notification_preferences_manager_id");

        builder.HasOne<Manager>().WithMany()
            .HasForeignKey(preferences => preferences.ManagerId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>comms.conversations</c>: a subject-bearing thread between two managers (`COM-10`).
/// </summary>
/// <remarks>
/// <para>
/// The list is read newest-activity-first for one manager on either side of the thread, so each side has its
/// own keyset index. The two managers differing is a check constraint as well as a domain rule, so no code
/// path, including a hand-written one, can store a conversation with oneself.
/// </para>
/// <para>
/// Every reference is restrictive: a conversation belongs to two people and is not removed when one account
/// is, which is the privacy workflow's decision to make, not a cascade's.
/// </para>
/// </remarks>
internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("conversations", "comms", table =>
        {
            table.HasCheckConstraint(
                "ck_conversations_participants",
                "started_by_manager_id <> recipient_manager_id");
            table.HasCheckConstraint(
                "ck_conversations_subject",
                $"length(subject) between 1 and {DirectMessageRules.SubjectMaxChars}");
            table.HasCheckConstraint("ck_conversations_version", "version >= 1");
        });

        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(conversation => conversation.Subject)
            .HasColumnName("subject")
            .HasMaxLength(DirectMessageRules.SubjectMaxChars)
            .IsRequired();
        builder.Property(conversation => conversation.StartedByManagerId)
            .HasColumnName("started_by_manager_id").IsRequired();
        builder.Property(conversation => conversation.RecipientManagerId)
            .HasColumnName("recipient_manager_id").IsRequired();
        builder.Property(conversation => conversation.StartedFromClubId)
            .HasColumnName("started_from_club_id").IsRequired();
        builder.Property(conversation => conversation.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(conversation => conversation.LastMessageAt).HasColumnName("last_message_at").IsRequired();
        builder.Property(conversation => conversation.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(conversation => conversation.Version).HasColumnName("version").IsRequired();

        // The list's read: one manager's conversations, most recently active first, from either side.
        builder.HasIndex(conversation => new
        {
            conversation.StartedByManagerId,
            conversation.LastMessageAt,
            conversation.Id,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_conversations_started_by_manager_id_last_message_at_id");

        builder.HasIndex(conversation => new
        {
            conversation.RecipientManagerId,
            conversation.LastMessageAt,
            conversation.Id,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_conversations_recipient_manager_id_last_message_at_id");

        builder.HasOne<Manager>().WithMany()
            .HasForeignKey(conversation => conversation.StartedByManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Manager>().WithMany()
            .HasForeignKey(conversation => conversation.RecipientManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany()
            .HasForeignKey(conversation => conversation.StartedFromClubId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>comms.direct_messages</c>: the plain-text messages of a conversation (`COM-13`, `COM-16`).
/// </summary>
/// <remarks>
/// The sync poll counts a manager's unread messages on every tick, so that count has a partial index on the
/// unread rows rather than scanning a manager's whole correspondence. A thread is read newest-first by send
/// time with the identity breaking a same-instant tie.
/// </remarks>
internal sealed class DirectMessageConfiguration : IEntityTypeConfiguration<DirectMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DirectMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("direct_messages", "comms", table =>
        {
            table.HasCheckConstraint("ck_direct_messages_participants", "sender_manager_id <> recipient_manager_id");
            table.HasCheckConstraint(
                "ck_direct_messages_body",
                $"length(body) between 1 and {DirectMessageRules.BodyMaxChars}");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(message => message.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(message => message.SenderManagerId).HasColumnName("sender_manager_id").IsRequired();
        builder.Property(message => message.RecipientManagerId).HasColumnName("recipient_manager_id").IsRequired();
        builder.Property(message => message.Body)
            .HasColumnName("body")
            .HasMaxLength(DirectMessageRules.BodyMaxChars)
            .IsRequired();
        builder.Property(message => message.SentAt).HasColumnName("sent_at").IsRequired();
        builder.Property(message => message.ReadAt).HasColumnName("read_at");

        // The thread's read: one conversation's messages, newest first.
        builder.HasIndex(message => new { message.ConversationId, message.SentAt, message.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_direct_messages_conversation_id_sent_at_id");

        // The badge's read: every unread message addressed to one manager, and nothing else.
        builder.HasIndex(message => message.RecipientManagerId)
            .HasDatabaseName("ix_direct_messages_recipient_manager_id_unread")
            .HasFilter("read_at is null");

        builder.HasOne<Conversation>().WithMany()
            .HasForeignKey(message => message.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Manager>().WithMany()
            .HasForeignKey(message => message.SenderManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Manager>().WithMany()
            .HasForeignKey(message => message.RecipientManagerId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <c>comms.manager_blocks</c>: who has blocked whom (`COM-15`).
/// </summary>
/// <remarks>
/// The pair is the key, so blocking twice is the same fact. The lookup that stops a send asks about both
/// directions, and the reverse direction is served by the second index.
/// </remarks>
internal sealed class ManagerBlockConfiguration : IEntityTypeConfiguration<ManagerBlock>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ManagerBlock> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("manager_blocks", "comms", table =>
            table.HasCheckConstraint("ck_manager_blocks_distinct", "blocker_manager_id <> blocked_manager_id"));

        builder.HasKey(block => new { block.BlockerManagerId, block.BlockedManagerId });
        builder.Property(block => block.BlockerManagerId).HasColumnName("blocker_manager_id");
        builder.Property(block => block.BlockedManagerId).HasColumnName("blocked_manager_id");
        builder.Property(block => block.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(block => block.BlockedManagerId)
            .HasDatabaseName("ix_manager_blocks_blocked_manager_id");

        builder.HasOne<Manager>().WithMany()
            .HasForeignKey(block => block.BlockerManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Manager>().WithMany()
            .HasForeignKey(block => block.BlockedManagerId).OnDelete(DeleteBehavior.Restrict);
    }
}

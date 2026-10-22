using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tactical11Manager.Domain.Forum;

namespace Tactical11Manager.Infrastructure.Persistence.Configurations;

/// <summary>Maps forum threads in the comms schema.</summary>
internal sealed class ForumThreadConfiguration : IEntityTypeConfiguration<ForumThread>
{
    public void Configure(EntityTypeBuilder<ForumThread> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("forum_threads", "comms", table =>
        {
            table.HasCheckConstraint("ck_forum_threads_title", $"length(title) between {ForumConstants.TitleMinChars} and {ForumConstants.TitleMaxChars}");
            table.HasCheckConstraint("ck_forum_threads_version", "version >= 1");
            table.HasCheckConstraint("ck_forum_threads_reply_count", "reply_count >= 0");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(t => t.SubforumId).HasColumnName("subforum_id").HasMaxLength(64).IsRequired();
        builder.Property(t => t.CountryId).HasColumnName("country_id");
        builder.Property(t => t.Title).HasColumnName("title").HasMaxLength(ForumConstants.TitleMaxChars).IsRequired();
        builder.Property(t => t.AuthorManagerId).HasColumnName("author_manager_id").IsRequired();
        builder.Property(t => t.AuthorManagerName).HasColumnName("author_manager_name").HasMaxLength(128).IsRequired();
        builder.Property(t => t.AuthorClubName).HasColumnName("author_club_name").HasMaxLength(128);
        builder.Property(t => t.IsPinned).HasColumnName("is_pinned").IsRequired();
        builder.Property(t => t.IsAnnouncement).HasColumnName("is_announcement").IsRequired();
        builder.Property(t => t.ReplyCount).HasColumnName("reply_count").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.LastActivityAt).HasColumnName("last_activity_at").IsRequired();
        builder.Property(t => t.LatestReplyAuthorName).HasColumnName("latest_reply_author_name").HasMaxLength(128);
        builder.Property(t => t.Version).HasColumnName("version").IsRequired();

        builder.HasIndex(t => new { t.SubforumId, t.IsPinned, t.LastActivityAt })
            .HasDatabaseName("ix_forum_threads_subforum_activity");
        builder.HasIndex(t => new { t.CountryId, t.SubforumId, t.LastActivityAt })
            .HasDatabaseName("ix_forum_threads_country_subforum");
    }
}

/// <summary>Maps forum posts in the comms schema.</summary>
internal sealed class ForumPostConfiguration : IEntityTypeConfiguration<ForumPost>
{
    public void Configure(EntityTypeBuilder<ForumPost> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("forum_posts", "comms", table =>
        {
            table.HasCheckConstraint("ck_forum_posts_post_number", "post_number >= 1");
            table.HasCheckConstraint("ck_forum_posts_body", $"length(body) between 1 and {ForumConstants.BodyMaxChars}");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(p => p.ThreadId).HasColumnName("thread_id").IsRequired();
        builder.Property(p => p.PostNumber).HasColumnName("post_number").IsRequired();
        builder.Property(p => p.AuthorManagerId).HasColumnName("author_manager_id").IsRequired();
        builder.Property(p => p.AuthorManagerName).HasColumnName("author_manager_name").HasMaxLength(128).IsRequired();
        builder.Property(p => p.AuthorClubName).HasColumnName("author_club_name").HasMaxLength(128);
        builder.Property(p => p.IsStaff).HasColumnName("is_staff").IsRequired();
        builder.Property(p => p.Body).HasColumnName("body").HasMaxLength(ForumConstants.BodyMaxChars).IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(p => new { p.ThreadId, p.PostNumber })
            .IsUnique()
            .HasDatabaseName("ux_forum_posts_thread_post_number");
    }
}

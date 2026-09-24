using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <c>auth.users</c>. The two unique indexes here are what make email and display name
/// identities, and <see cref="User.Version"/> is the concurrency token behind <c>If-Match</c>
/// (ADR-0009, CONC-1).
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users", "auth", table =>
        {
            table.HasCheckConstraint("ck_users_failed_login_count", "failed_login_count >= 0");
            table.HasCheckConstraint(
                "ck_users_status",
                "status in ('pending', 'active', 'suspended', 'deletion_pending', 'anonymized')");
        });

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(254).IsRequired();
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
        builder.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(32).IsRequired();
        builder.Property(user => user.NormalizedDisplayName)
            .HasColumnName("normalized_display_name")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(user => user.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(user => user.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion(status => status.ToCode(), code => UserStatuses.FromCode(code))
            .IsRequired();
        builder.Property(user => user.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(user => user.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(64).IsRequired();
        builder.Property(user => user.FailedLoginCount).HasColumnName("failed_login_count").IsRequired();
        builder.Property(user => user.LockoutUntil).HasColumnName("lockout_until");
        builder.Property(user => user.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(user => user.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(user => user.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_email");

        builder.HasIndex(user => user.NormalizedDisplayName)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_display_name");

        // Roles are always loaded with their account: they are a handful of rows and an account
        // without its roles cannot be authorized.
        builder.HasMany(user => user.Roles)
            .WithOne()
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The property is exposed as IReadOnlyCollection, so the persisted collection is the backing
        // field rather than a settable property.
        builder.Navigation(user => user.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Maps <c>auth.user_roles</c>.</summary>
internal sealed class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_roles", "auth");

        builder.HasKey(assignment => new { assignment.UserId, assignment.Role });

        builder.Property(assignment => assignment.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(assignment => assignment.Role).HasColumnName("role").HasMaxLength(32).IsRequired();
        builder.Property(assignment => assignment.GrantedAt).HasColumnName("granted_at").IsRequired();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class AuthSessionRowConfiguration : IEntityTypeConfiguration<AuthSessionRow>
{
    public void Configure(EntityTypeBuilder<AuthSessionRow> e)
    {
        e.ToTable("auth_sessions");
        e.HasKey(x => x.Token);
        e.Property(x => x.Token).HasMaxLength(64);
        e.Property(x => x.User)
            .HasColumnName("UserJson")
            .HasColumnType("jsonb")
            .HasConversion(EntityValueConversions.SessionUser())
            .Metadata.SetValueComparer(EntityValueConversions.SessionUserComparer());
        e.Property(x => x.ExpiresAt);
        e.Property(x => x.CreatedAt);
        e.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class AuthPendingOtpRowConfiguration : IEntityTypeConfiguration<AuthPendingOtpRow>
{
    public void Configure(EntityTypeBuilder<AuthPendingOtpRow> e)
    {
        e.ToTable("auth_pending_otps");
        e.HasKey(x => x.PhoneDigits);
        e.Property(x => x.PhoneDigits).HasMaxLength(32);
        e.Property(x => x.Code).HasMaxLength(32);
        e.Property(x => x.ExpiresAt);
        e.Property(x => x.CreatedAt);
        e.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class AuthPendingRegistrationRowConfiguration : IEntityTypeConfiguration<AuthPendingRegistrationRow>
{
    public void Configure(EntityTypeBuilder<AuthPendingRegistrationRow> e)
    {
        e.ToTable("auth_pending_registrations");
        e.HasKey(x => x.RegistrationId);
        e.Property(x => x.RegistrationId).HasMaxLength(64);
        e.Property(x => x.PasswordHash).HasMaxLength(512);
        e.Property(x => x.Email).HasMaxLength(320);
        e.Property(x => x.Username).HasMaxLength(32);
        e.Property(x => x.PhoneDigits).HasMaxLength(32);
        e.Property(x => x.PhoneDisplay).HasMaxLength(64);
        e.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class AuthPendingEmailOtpRowConfiguration : IEntityTypeConfiguration<AuthPendingEmailOtpRow>
{
    public void Configure(EntityTypeBuilder<AuthPendingEmailOtpRow> e)
    {
        e.ToTable("auth_pending_email_otps");
        e.HasKey(x => x.Key);
        e.Property(x => x.Key).HasMaxLength(128);
        e.Property(x => x.Purpose).HasMaxLength(32);
        e.Property(x => x.Code).HasMaxLength(32);
        e.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class AuthPendingPasswordResetRowConfiguration : IEntityTypeConfiguration<AuthPendingPasswordResetRow>
{
    public void Configure(EntityTypeBuilder<AuthPendingPasswordResetRow> e)
    {
        e.ToTable("auth_pending_password_resets");
        e.HasKey(x => x.Email);
        e.Property(x => x.Email).HasMaxLength(320);
        e.Property(x => x.NewPasswordHash).HasMaxLength(512);
        e.Property(x => x.Code).HasMaxLength(32);
        e.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class UserContactRowConfiguration : IEntityTypeConfiguration<UserContactRow>
{
    public void Configure(EntityTypeBuilder<UserContactRow> e)
    {
        e.ToTable("user_contacts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.OwnerUserId).HasMaxLength(64);
        e.Property(x => x.ContactUserId).HasMaxLength(64);
        e.Property(x => x.CreatedAt);
        e.Property(x => x.DeletedAtUtc);
        e.HasQueryFilter(c => c.DeletedAtUtc == null);
        e.HasIndex(x => x.OwnerUserId);
        e.HasIndex(x => new { x.OwnerUserId, x.ContactUserId })
            .IsUnique()
            .HasFilter("\"DeletedAtUtc\" IS NULL");
        e.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.ContactUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

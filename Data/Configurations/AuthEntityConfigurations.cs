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

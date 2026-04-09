using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MarketWorkspaceRow> MarketWorkspaces => Set<MarketWorkspaceRow>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<StoreRow> Stores => Set<StoreRow>();
    public DbSet<StoreProductRow> StoreProducts => Set<StoreProductRow>();
    public DbSet<StoreServiceRow> StoreServices => Set<StoreServiceRow>();
    public DbSet<StoredMediaRow> StoredMedia => Set<StoredMediaRow>();
    public DbSet<AuthSessionRow> AuthSessions => Set<AuthSessionRow>();
    public DbSet<AuthPendingOtpRow> AuthPendingOtps => Set<AuthPendingOtpRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketWorkspaceRow>(e =>
        {
            e.ToTable("market_workspaces");
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.UpdatedAt);
        });

        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("user_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.PhoneDigits).HasMaxLength(32);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.PhoneDisplay).HasMaxLength(64);
            // Can store `data:` URLs (base64), which frequently exceed 2048 chars.
            e.Property(x => x.AvatarUrl).HasColumnType("text");
            e.Property(x => x.Instagram).HasMaxLength(256);
            e.Property(x => x.Telegram).HasMaxLength(256);
            e.Property(x => x.XAccount).HasMaxLength(256);
            e.HasIndex(x => x.PhoneDigits)
                .IsUnique()
                .HasFilter("\"PhoneDigits\" IS NOT NULL");
            e.HasMany(x => x.Stores)
                .WithOne(x => x.Owner)
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StoreRow>(e =>
        {
            e.ToTable("stores");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OwnerUserId).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(512);
            e.Property(x => x.NormalizedName).HasMaxLength(512);
            e.HasIndex(x => x.NormalizedName)
                .IsUnique()
                .HasFilter("\"NormalizedName\" IS NOT NULL");
            // Can store `data:` URLs (base64), which frequently exceed 2048 chars.
            e.Property(x => x.AvatarUrl).HasColumnType("text");
            e.Property(x => x.CategoriesJson).HasColumnType("jsonb");
            e.Property(x => x.Pitch);
            e.HasIndex(x => x.OwnerUserId);
            e.HasMany(x => x.Products)
                .WithOne(x => x.Store)
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Services)
                .WithOne(x => x.Store)
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StoreProductRow>(e =>
        {
            e.ToTable("store_products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.StoreId).HasMaxLength(64);
            e.Property(x => x.MonedaPrecio).HasMaxLength(16);
            e.Property(x => x.MonedasJson).HasColumnType("jsonb");
            e.Property(x => x.PhotoUrlsJson).HasColumnType("jsonb");
            e.Property(x => x.CustomFieldsJson).HasColumnType("jsonb");
            e.HasIndex(x => x.StoreId);
        });

        modelBuilder.Entity<StoreServiceRow>(e =>
        {
            e.ToTable("store_services");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.StoreId).HasMaxLength(64);
            e.Property(x => x.RiesgosJson).HasColumnType("jsonb");
            e.Property(x => x.DependenciasJson).HasColumnType("jsonb");
            e.Property(x => x.GarantiasJson).HasColumnType("jsonb");
            e.Property(x => x.CustomFieldsJson).HasColumnType("jsonb");
            e.HasIndex(x => x.StoreId);
        });

        modelBuilder.Entity<StoredMediaRow>(e =>
        {
            e.ToTable("stored_media");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.MimeType).HasMaxLength(256);
            e.Property(x => x.FileName).HasMaxLength(512);
            e.Property(x => x.SizeBytes);
            e.Property(x => x.Bytes).HasColumnType("bytea");
            e.Property(x => x.CreatedAt);
        });

        modelBuilder.Entity<AuthSessionRow>(e =>
        {
            e.ToTable("auth_sessions");
            e.HasKey(x => x.Token);
            e.Property(x => x.Token).HasMaxLength(64);
            e.Property(x => x.UserJson).HasColumnType("jsonb");
            e.Property(x => x.ExpiresAt);
            e.Property(x => x.CreatedAt);
            e.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<AuthPendingOtpRow>(e =>
        {
            e.ToTable("auth_pending_otps");
            e.HasKey(x => x.PhoneDigits);
            e.Property(x => x.PhoneDigits).HasMaxLength(32);
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.ExpiresAt);
            e.Property(x => x.CreatedAt);
            e.HasIndex(x => x.ExpiresAt);
        });
    }
}

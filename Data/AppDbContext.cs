using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;

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
    public DbSet<UserContactRow> UserContacts => Set<UserContactRow>();
    public DbSet<UserOfferInteractionRow> UserOfferInteractions => Set<UserOfferInteractionRow>();
    public DbSet<ChatThreadRow> ChatThreads => Set<ChatThreadRow>();
    public DbSet<ChatMessageRow> ChatMessages => Set<ChatMessageRow>();
    public DbSet<ChatNotificationRow> ChatNotifications => Set<ChatNotificationRow>();
    public DbSet<OfferLikeRow> OfferLikes => Set<OfferLikeRow>();
    public DbSet<OfferQaCommentLikeRow> OfferQaCommentLikes => Set<OfferQaCommentLikeRow>();

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
            e.Property(x => x.SavedOfferIdsJson).HasColumnType("jsonb");
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
            e.Property(x => x.WebsiteUrl).HasMaxLength(2048);
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
            e.Property(x => x.OfferQa)
                .HasColumnName("OfferQaJson")
                .HasColumnType("jsonb")
                .HasConversion(OfferQaJson.CreateEfConverter());
            e.Property(x => x.PopularityWeight).HasDefaultValue(0d);
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
            e.Property(x => x.MonedasJson).HasColumnType("jsonb");
            e.Property(x => x.CustomFieldsJson).HasColumnType("jsonb");
            e.Property(x => x.PhotoUrlsJson).HasColumnType("jsonb");
            e.Property(x => x.OfferQa)
                .HasColumnName("OfferQaJson")
                .HasColumnType("jsonb")
                .HasConversion(OfferQaJson.CreateEfConverter());
            e.Property(x => x.PopularityWeight).HasDefaultValue(0d);
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

        modelBuilder.Entity<UserContactRow>(e =>
        {
            e.ToTable("user_contacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OwnerUserId).HasMaxLength(64);
            e.Property(x => x.ContactUserId).HasMaxLength(64);
            e.Property(x => x.CreatedAt);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => new { x.OwnerUserId, x.ContactUserId }).IsUnique();
            e.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.ContactUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserOfferInteractionRow>(e =>
        {
            e.ToTable("user_offer_interactions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.OfferId).HasMaxLength(64);
            e.Property(x => x.EventType).HasMaxLength(32);
            e.Property(x => x.CreatedAt);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.OfferId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.OfferId, x.CreatedAt });
        });

        modelBuilder.Entity<ChatThreadRow>(e =>
        {
            e.ToTable("chat_threads");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OfferId).HasMaxLength(64);
            e.Property(x => x.StoreId).HasMaxLength(64);
            e.Property(x => x.BuyerUserId).HasMaxLength(64);
            e.Property(x => x.SellerUserId).HasMaxLength(64);
            e.Property(x => x.InitiatorUserId).HasMaxLength(64);
            e.Property(x => x.FirstMessageSentAtUtc);
            e.Property(x => x.PurchaseMode);
            e.Property(x => x.CreatedAtUtc);
            e.Property(x => x.DeletedAtUtc);
            e.HasIndex(x => x.OfferId);
            e.HasIndex(x => x.BuyerUserId);
            e.HasIndex(x => x.SellerUserId);
            e.HasIndex(x => new { x.OfferId, x.BuyerUserId })
                .IsUnique()
                .HasFilter("\"DeletedAtUtc\" IS NULL");
            e.HasMany(x => x.Messages)
                .WithOne(x => x.Thread)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessageRow>(e =>
        {
            e.ToTable("chat_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ThreadId).HasMaxLength(64);
            e.Property(x => x.SenderUserId).HasMaxLength(64);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.CreatedAtUtc);
            e.Property(x => x.UpdatedAtUtc);
            e.Property(x => x.DeletedAtUtc);
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => new { x.ThreadId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<ChatNotificationRow>(e =>
        {
            e.ToTable("chat_notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.RecipientUserId).HasMaxLength(64);
            e.Property(x => x.ThreadId).HasMaxLength(64);
            e.Property(x => x.MessageId).HasMaxLength(64);
            e.Property(x => x.OfferId).HasMaxLength(64);
            e.Property(x => x.MessagePreview).HasMaxLength(2000);
            e.Property(x => x.AuthorStoreName).HasMaxLength(512);
            e.Property(x => x.SenderUserId).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.CreatedAtUtc);
            e.Property(x => x.ReadAtUtc);
            e.HasIndex(x => x.RecipientUserId);
            e.HasIndex(x => new { x.RecipientUserId, x.CreatedAtUtc });
            e.HasIndex(x => x.OfferId);
        });

        modelBuilder.Entity<OfferLikeRow>(e =>
        {
            e.ToTable("offer_likes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OfferId).HasMaxLength(64);
            e.Property(x => x.LikerKey).HasMaxLength(96);
            e.Property(x => x.CreatedAtUtc);
            e.HasIndex(x => x.OfferId);
            e.HasIndex(x => new { x.OfferId, x.LikerKey }).IsUnique();
            e.HasIndex(x => x.LikerKey);
        });

        modelBuilder.Entity<OfferQaCommentLikeRow>(e =>
        {
            e.ToTable("offer_qa_comment_likes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OfferId).HasMaxLength(64);
            e.Property(x => x.QaCommentId).HasMaxLength(64);
            e.Property(x => x.LikerKey).HasMaxLength(96);
            e.Property(x => x.CreatedAtUtc);
            e.HasIndex(x => new { x.OfferId, x.QaCommentId });
            e.HasIndex(x => new { x.OfferId, x.QaCommentId, x.LikerKey }).IsUnique();
            e.HasIndex(x => x.LikerKey);
        });
    }
}

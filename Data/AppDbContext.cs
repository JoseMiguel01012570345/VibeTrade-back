using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Domain.Market;
using VibeTrade.Backend.Data.RouteSheets;

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
    public DbSet<TradeAgreementRow> TradeAgreements => Set<TradeAgreementRow>();
    public DbSet<ChatRouteSheetRow> ChatRouteSheets => Set<ChatRouteSheetRow>();
    public DbSet<EmergentOfferRow> EmergentOffers => Set<EmergentOfferRow>();
    public DbSet<TradeAgreementMerchandiseLineRow> TradeAgreementMerchandiseLines => Set<TradeAgreementMerchandiseLineRow>();
    public DbSet<TradeAgreementMerchandiseMetaRow> TradeAgreementMerchandiseMetas => Set<TradeAgreementMerchandiseMetaRow>();
    public DbSet<TradeAgreementServiceItemRow> TradeAgreementServiceItems => Set<TradeAgreementServiceItemRow>();
    public DbSet<TradeAgreementExtraFieldRow> TradeAgreementExtraFields => Set<TradeAgreementExtraFieldRow>();
    public DbSet<ChatNotificationRow> ChatNotifications => Set<ChatNotificationRow>();
    public DbSet<RouteTramoSubscriptionRow> RouteTramoSubscriptions => Set<RouteTramoSubscriptionRow>();
    public DbSet<OfferLikeRow> OfferLikes => Set<OfferLikeRow>();
    public DbSet<OfferQaCommentLikeRow> OfferQaCommentLikes => Set<OfferQaCommentLikeRow>();
    public DbSet<TrustScoreLedgerRow> TrustScoreLedgerRows => Set<TrustScoreLedgerRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketWorkspaceRow>(e =>
        {
            e.ToTable("market_workspaces");
            e.HasKey(x => x.Id);
            e.Property(x => x.State)
                .HasColumnName("Payload")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.MarketWorkspace())
                .Metadata.SetValueComparer(EntityValueConversions.MarketWorkspaceComparer());
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
            e.Property(x => x.SavedOfferIds)
                .HasColumnName("SavedOfferIdsJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.StringList())
                .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
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
            e.Property(x => x.Categories)
                .HasColumnName("CategoriesJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.StringList())
                .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
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
            e.Property(x => x.Monedas)
                .HasColumnName("MonedasJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.StringList())
                .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
            e.Property(x => x.PhotoUrls)
                .HasColumnName("PhotoUrlsJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.StringList())
                .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
            e.Property(x => x.CustomFields)
                .HasColumnName("CustomFieldsJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.CustomFields())
                .Metadata.SetValueComparer(EntityValueConversions.CustomFieldsComparer());
            e.Property(x => x.OfferQa)
                .HasColumnName("OfferQaJson")
                .HasColumnType("jsonb")
                .HasConversion(OfferQaJson.CreateEfConverter());
            e.Property(x => x.PopularityWeight).HasDefaultValue(0d);
            e.Property(x => x.DeletedAtUtc);
            e.HasQueryFilter(p => p.DeletedAtUtc == null);
            e.HasIndex(x => x.StoreId);
        });

        modelBuilder.Entity<StoreServiceRow>(e =>
        {
            e.ToTable("store_services");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.StoreId).HasMaxLength(64);
            e.Property(x => x.Riesgos)
                .HasColumnName("RiesgosJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.ServiceRiesgos())
                .Metadata.SetValueComparer(EntityValueConversions.ServiceRiesgosComparer());
            e.Property(x => x.Dependencias)
                .HasColumnName("DependenciasJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.ServiceDependencias())
                .Metadata.SetValueComparer(EntityValueConversions.ServiceDependenciasComparer());
            e.Property(x => x.Garantias)
                .HasColumnName("GarantiasJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.ServiceGarantias())
                .Metadata.SetValueComparer(EntityValueConversions.ServiceGarantiasComparer());
            e.Property(x => x.Monedas)
                .HasColumnName("MonedasJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.StringList())
                .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
            e.Property(x => x.CustomFields)
                .HasColumnName("CustomFieldsJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.CustomFields())
                .Metadata.SetValueComparer(EntityValueConversions.CustomFieldsComparer());
            e.Property(x => x.PhotoUrls)
                .HasColumnName("PhotoUrlsJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.StringList())
                .Metadata.SetValueComparer(EntityValueConversions.StringListComparer());
            e.Property(x => x.OfferQa)
                .HasColumnName("OfferQaJson")
                .HasColumnType("jsonb")
                .HasConversion(OfferQaJson.CreateEfConverter());
            e.Property(x => x.PopularityWeight).HasDefaultValue(0d);
            e.Property(x => x.DeletedAtUtc);
            e.HasQueryFilter(s => s.DeletedAtUtc == null);
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
            e.Property(x => x.User)
                .HasColumnName("UserJson")
                .HasColumnType("jsonb")
                .HasConversion(EntityValueConversions.SessionUser())
                .Metadata.SetValueComparer(EntityValueConversions.SessionUserComparer());
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
            e.Property(x => x.BuyerExpelledAtUtc);
            e.Property(x => x.SellerExpelledAtUtc);
            e.Property(x => x.PartyExitedUserId).HasMaxLength(64);
            e.Property(x => x.PartyExitedReason).HasMaxLength(2000);
            e.Property(x => x.PartyExitedAtUtc);
            e.HasIndex(x => x.OfferId);
            e.HasIndex(x => x.BuyerUserId);
            e.HasIndex(x => x.SellerUserId);
            // Múltiples hilos activos (misma oferta y comprador) al iniciar "Comprar" de nuevo, sin archivar.
            e.HasIndex(x => new { x.OfferId, x.BuyerUserId })
                .HasFilter("\"DeletedAtUtc\" IS NULL");
            e.HasMany(x => x.Messages)
                .WithOne(x => x.Thread)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.TradeAgreements)
                .WithOne(x => x.Thread)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatRouteSheetRow>(e =>
        {
            e.ToTable("chat_route_sheets");
            e.HasKey(x => new { x.ThreadId, x.RouteSheetId });
            e.Property(x => x.ThreadId).HasMaxLength(64);
            e.Property(x => x.RouteSheetId).HasMaxLength(64);
            e.Property(x => x.Payload)
                .HasColumnName("PayloadJson")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, RouteSheetJson.Options),
                    v => string.IsNullOrWhiteSpace(v)
                        ? new RouteSheetPayload()
                        : JsonSerializer.Deserialize<RouteSheetPayload>(v, RouteSheetJson.Options)
                            ?? new RouteSheetPayload());
            e.Property(x => x.UpdatedAtUtc);
            e.Property(x => x.DeletedAtUtc);
            e.Property(x => x.DeletedByUserId).HasMaxLength(64);
            e.HasIndex(x => x.ThreadId);
            e.HasOne<ChatThreadRow>()
                .WithMany()
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RouteTramoSubscriptionRow>(e =>
        {
            e.ToTable("route_tramo_subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(48);
            e.Property(x => x.ThreadId).HasMaxLength(64);
            e.Property(x => x.RouteSheetId).HasMaxLength(64);
            e.Property(x => x.StopId).HasMaxLength(64);
            e.Property(x => x.CarrierUserId).HasMaxLength(64);
            e.Property(x => x.CarrierPhoneSnapshot).HasMaxLength(40);
            e.Property(x => x.StoreServiceId).HasMaxLength(64);
            e.Property(x => x.TransportServiceLabel).HasMaxLength(512);
            e.Property(x => x.Status).HasMaxLength(24);
            e.Property(x => x.StopContentFingerprint).HasMaxLength(2048);
            e.Property(x => x.CreatedAtUtc);
            e.Property(x => x.UpdatedAtUtc);
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => new { x.ThreadId, x.RouteSheetId, x.StopId, x.CarrierUserId });
            e.HasOne<ChatThreadRow>()
                .WithMany()
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmergentOfferRow>(e =>
        {
            e.ToTable("emergent_offers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.ThreadId).HasMaxLength(64);
            e.Property(x => x.OfferId).HasMaxLength(64);
            e.Property(x => x.RouteSheetId).HasMaxLength(64);
            e.Property(x => x.PublisherUserId).HasMaxLength(64);
            e.Property(x => x.RouteSheetSnapshot)
                .HasColumnName("SnapshotJson")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, RouteSheetJson.Options),
                    v => string.IsNullOrWhiteSpace(v)
                        ? new EmergentRouteSheetSnapshot()
                        : JsonSerializer.Deserialize<EmergentRouteSheetSnapshot>(v, RouteSheetJson.Options)
                            ?? new EmergentRouteSheetSnapshot());
            e.Property(x => x.PublishedAtUtc);
            e.Property(x => x.RetractedAtUtc);
            e.Property(x => x.OfferQa)
                .HasColumnName("OfferQaJson")
                .HasColumnType("jsonb")
                .HasConversion(OfferQaJson.CreateEfConverter());
            e.HasIndex(x => new { x.ThreadId, x.RouteSheetId }).IsUnique();
            e.HasIndex(x => x.OfferId);
            e.HasIndex(x => new { x.Kind, x.RetractedAtUtc, x.PublishedAtUtc });
        });

        modelBuilder.Entity<TradeAgreementRow>(e =>
        {
            e.ToTable("trade_agreements");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ThreadId).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.IssuedByStoreId).HasMaxLength(64);
            e.Property(x => x.IssuerLabel).HasMaxLength(512);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.RespondedByUserId).HasMaxLength(64);
            e.Property(x => x.RouteSheetId).HasMaxLength(64);
            e.Property(x => x.RouteSheetUrl).HasColumnType("text");
            e.Property(x => x.DeletedByUserId).HasMaxLength(64);
            e.Property(x => x.HadBuyerAcceptance).HasColumnType("boolean");
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => new { x.ThreadId, x.Status });
            e.HasIndex(x => x.DeletedAtUtc);
            e.HasMany(x => x.MerchandiseLines)
                .WithOne(x => x.TradeAgreement)
                .HasForeignKey(x => x.TradeAgreementId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MerchandiseMeta)
                .WithOne(x => x.TradeAgreement)
                .HasForeignKey<TradeAgreementMerchandiseMetaRow>(x => x.TradeAgreementId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ServiceItems)
                .WithOne(x => x.TradeAgreement)
                .HasForeignKey(x => x.TradeAgreementId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ExtraFields)
                .WithOne(x => x.TradeAgreement)
                .HasForeignKey(x => x.TradeAgreementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TradeAgreementExtraFieldRow>(e =>
        {
            e.ToTable("trade_agreement_extra_fields");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.TradeAgreementId).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(256);
            e.Property(x => x.ValueKind).HasMaxLength(16);
            e.Property(x => x.TextValue).HasColumnType("text");
            e.Property(x => x.MediaUrl).HasColumnType("text");
            e.Property(x => x.FileName).HasMaxLength(512);
            e.Property(x => x.Scope).HasMaxLength(32);
            e.HasIndex(x => x.TradeAgreementId);
        });

        modelBuilder.Entity<TradeAgreementMerchandiseLineRow>(e =>
        {
            e.ToTable("trade_agreement_merchandise_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.TradeAgreementId).HasMaxLength(64);
            e.Property(x => x.LinkedStoreProductId).HasMaxLength(64);
            e.Property(x => x.Tipo).HasMaxLength(512);
            e.Property(x => x.Cantidad).HasMaxLength(128);
            e.Property(x => x.ValorUnitario).HasMaxLength(128);
            e.Property(x => x.Estado).HasMaxLength(32);
            e.Property(x => x.Descuento).HasMaxLength(128);
            e.Property(x => x.Impuestos).HasMaxLength(128);
            e.Property(x => x.Moneda).HasMaxLength(32);
            e.Property(x => x.TipoEmbalaje).HasMaxLength(256);
            e.Property(x => x.DevolucionesDesc).HasColumnType("text");
            e.Property(x => x.DevolucionQuienPaga).HasMaxLength(256);
            e.Property(x => x.DevolucionPlazos).HasMaxLength(256);
            e.Property(x => x.Regulaciones).HasColumnType("text");
            e.HasIndex(x => x.TradeAgreementId);
        });

        modelBuilder.Entity<TradeAgreementMerchandiseMetaRow>(e =>
        {
            e.ToTable("trade_agreement_merchandise_metas");
            e.HasKey(x => x.TradeAgreementId);
            e.Property(x => x.TradeAgreementId).HasMaxLength(64);
            e.Property(x => x.Moneda).HasMaxLength(32);
            e.Property(x => x.TipoEmbalaje).HasMaxLength(256);
            e.Property(x => x.DevolucionesDesc).HasColumnType("text");
            e.Property(x => x.DevolucionQuienPaga).HasMaxLength(256);
            e.Property(x => x.DevolucionPlazos).HasMaxLength(256);
            e.Property(x => x.Regulaciones).HasColumnType("text");
        });

        modelBuilder.Entity<TradeAgreementServiceItemRow>(e =>
        {
            e.ToTable("trade_agreement_service_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(80);
            e.Property(x => x.TradeAgreementId).HasMaxLength(64);
            e.Property(x => x.LinkedStoreServiceId).HasMaxLength(64);
            e.Property(x => x.TipoServicio).HasMaxLength(512);
            e.Property(x => x.TiempoStartDate).HasMaxLength(64);
            e.Property(x => x.TiempoEndDate).HasMaxLength(64);
            e.Property(x => x.Descripcion).HasColumnType("text");
            e.Property(x => x.Incluye).HasColumnType("text");
            e.Property(x => x.NoIncluye).HasColumnType("text");
            e.Property(x => x.Entregables).HasColumnType("text");
            e.Property(x => x.MetodoPago).HasMaxLength(256);
            e.Property(x => x.Moneda).HasMaxLength(128);
            e.Property(x => x.MedicionCumplimiento).HasColumnType("text");
            e.Property(x => x.PenalIncumplimiento).HasColumnType("text");
            e.Property(x => x.NivelResponsabilidad).HasColumnType("text");
            e.Property(x => x.PropIntelectual).HasColumnType("text");
            e.Property(x => x.CondicionesExtrasJson).HasColumnType("text");
            e.Property(x => x.ScheduleDefaultWindowStart).HasMaxLength(16);
            e.Property(x => x.ScheduleDefaultWindowEnd).HasMaxLength(16);
            e.Property(x => x.GarantiasTexto).HasColumnType("text");
            e.Property(x => x.PenalAtrasoTexto).HasColumnType("text");
            e.Property(x => x.TerminacionAvisoDias).HasMaxLength(64);
            e.HasIndex(x => x.TradeAgreementId);
            e.HasMany(x => x.ScheduleMonths)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ScheduleDays)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ScheduleOverrides)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.PaymentMonths)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.PaymentEntries)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.RiesgoItems)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.DependenciaItems)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.TerminacionCausas)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MonedasAceptadas)
                .WithOne(x => x.ServiceItem)
                .HasForeignKey(x => x.ServiceItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TradeAgreementServiceScheduleMonthRow>(e =>
        {
            e.ToTable("trade_agreement_service_schedule_months");
            e.HasKey(x => new { x.ServiceItemId, x.Month });
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
        });

        modelBuilder.Entity<TradeAgreementServiceScheduleDayRow>(e =>
        {
            e.ToTable("trade_agreement_service_schedule_days");
            e.HasKey(x => new { x.ServiceItemId, x.Month, x.CalendarDay });
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
        });

        modelBuilder.Entity<TradeAgreementServiceScheduleOverrideRow>(e =>
        {
            e.ToTable("trade_agreement_service_schedule_overrides");
            e.HasKey(x => new { x.ServiceItemId, x.Month, x.CalendarDay });
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
            e.Property(x => x.WindowStart).HasMaxLength(16);
            e.Property(x => x.WindowEnd).HasMaxLength(16);
        });

        modelBuilder.Entity<TradeAgreementServicePaymentMonthRow>(e =>
        {
            e.ToTable("trade_agreement_service_payment_months");
            e.HasKey(x => new { x.ServiceItemId, x.Month });
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
        });

        modelBuilder.Entity<TradeAgreementServicePaymentEntryRow>(e =>
        {
            e.ToTable("trade_agreement_service_payment_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
            e.Property(x => x.Amount).HasMaxLength(64);
            e.Property(x => x.Moneda).HasMaxLength(32);
            e.HasIndex(x => x.ServiceItemId);
        });

        modelBuilder.Entity<TradeAgreementServiceRiesgoRow>(e =>
        {
            e.ToTable("trade_agreement_service_riesgos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
            e.Property(x => x.Text).HasColumnType("text");
            e.HasIndex(x => x.ServiceItemId);
        });

        modelBuilder.Entity<TradeAgreementServiceDependenciaRow>(e =>
        {
            e.ToTable("trade_agreement_service_dependencias");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
            e.Property(x => x.Text).HasColumnType("text");
            e.HasIndex(x => x.ServiceItemId);
        });

        modelBuilder.Entity<TradeAgreementServiceTerminacionCausaRow>(e =>
        {
            e.ToTable("trade_agreement_service_terminacion_causas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
            e.Property(x => x.Text).HasColumnType("text");
            e.HasIndex(x => x.ServiceItemId);
        });

        modelBuilder.Entity<TradeAgreementServiceMonedaRow>(e =>
        {
            e.ToTable("trade_agreement_service_monedas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ServiceItemId).HasMaxLength(80);
            e.Property(x => x.Code).HasMaxLength(16);
            e.HasIndex(x => x.ServiceItemId);
        });

        modelBuilder.Entity<ChatMessageRow>(e =>
        {
            e.ToTable("chat_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ThreadId).HasMaxLength(64);
            e.Property(x => x.SenderUserId).HasMaxLength(64);
            e.Property(x => x.Payload)
                .HasColumnName("PayloadJson")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, ChatMessageJson.Options),
                    v => ChatMessageJson.DeserializePayload(v));
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.CreatedAtUtc);
            e.Property(x => x.UpdatedAtUtc);
            e.Property(x => x.DeletedAtUtc);
            e.Property(x => x.GroupReceiptsJson);
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
            e.Property(x => x.MetaJson).HasMaxLength(4000);
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

        modelBuilder.Entity<TrustScoreLedgerRow>(e =>
        {
            e.ToTable("trust_score_ledger");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.SubjectType).HasMaxLength(16);
            e.Property(x => x.SubjectId).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(512);
            e.HasIndex(x => new { x.SubjectType, x.SubjectId, x.CreatedAtUtc });
        });
    }
}

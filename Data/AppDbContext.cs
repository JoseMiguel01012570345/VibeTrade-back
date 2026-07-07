using Microsoft.EntityFrameworkCore;

namespace VibeTrade.Backend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MarketWorkspaceRow> MarketWorkspaces => Set<MarketWorkspaceRow>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<StoreRow> Stores => Set<StoreRow>();
    public DbSet<StoreProductRow> StoreProducts => Set<StoreProductRow>();
    public DbSet<StoreServiceRow> StoreServices => Set<StoreServiceRow>();
    public DbSet<StoreCategoryRow> StoreCategories => Set<StoreCategoryRow>();
    public DbSet<StoreSupplierRow> StoreSuppliers => Set<StoreSupplierRow>();
    public DbSet<StoreBannerRow> StoreBanners => Set<StoreBannerRow>();
    public DbSet<StoredMediaRow> StoredMedia => Set<StoredMediaRow>();
    public DbSet<AuthSessionRow> AuthSessions => Set<AuthSessionRow>();
    public DbSet<AuthPendingOtpRow> AuthPendingOtps => Set<AuthPendingOtpRow>();
    public DbSet<AuthPendingRegistrationRow> AuthPendingRegistrations => Set<AuthPendingRegistrationRow>();
    public DbSet<AuthPendingEmailOtpRow> AuthPendingEmailOtps => Set<AuthPendingEmailOtpRow>();
    public DbSet<AuthPendingPasswordResetRow> AuthPendingPasswordResets => Set<AuthPendingPasswordResetRow>();
    public DbSet<UserContactRow> UserContacts => Set<UserContactRow>();
    public DbSet<UserOfferInteractionRow> UserOfferInteractions => Set<UserOfferInteractionRow>();
    public DbSet<ChatThreadRow> ChatThreads => Set<ChatThreadRow>();
    public DbSet<ChatSocialGroupMemberRow> ChatSocialGroupMembers => Set<ChatSocialGroupMemberRow>();
    public DbSet<ChatMessageRow> ChatMessages => Set<ChatMessageRow>();
    public DbSet<TradeAgreementRow> TradeAgreements => Set<TradeAgreementRow>();
    public DbSet<ChatRouteSheetRow> ChatRouteSheets => Set<ChatRouteSheetRow>();
    public DbSet<EmergentOfferRow> EmergentOffers => Set<EmergentOfferRow>();
    public DbSet<TradeAgreementServiceItemRow> TradeAgreementServiceItems => Set<TradeAgreementServiceItemRow>();
    public DbSet<TradeAgreementExtraFieldRow> TradeAgreementExtraFields => Set<TradeAgreementExtraFieldRow>();
    public DbSet<ChatNotificationRow> ChatNotifications => Set<ChatNotificationRow>();
    public DbSet<RouteTramoSubscriptionRow> RouteTramoSubscriptions => Set<RouteTramoSubscriptionRow>();
    public DbSet<OfferLikeRow> OfferLikes => Set<OfferLikeRow>();
    public DbSet<StoreCommentLikeRow> StoreCommentLikes => Set<StoreCommentLikeRow>();
    public DbSet<TrustScoreLedgerRow> TrustScoreLedgerRows => Set<TrustScoreLedgerRow>();
    public DbSet<MensualidadPaymentRow> MensualidadPayments => Set<MensualidadPaymentRow>();
    public DbSet<AgreementCurrencyPaymentRow> AgreementCurrencyPayments => Set<AgreementCurrencyPaymentRow>();
    public DbSet<AgreementRouteLegPaidRow> AgreementRouteLegPaids => Set<AgreementRouteLegPaidRow>();
    public DbSet<AgreementServicePaymentRow> AgreementServicePayments => Set<AgreementServicePaymentRow>();
    public DbSet<ServiceEvidenceRow> ServiceEvidences => Set<ServiceEvidenceRow>();
    public DbSet<RouteStopDeliveryRow> RouteStopDeliveries => Set<RouteStopDeliveryRow>();
    public DbSet<CarrierOwnershipEventRow> CarrierOwnershipEvents => Set<CarrierOwnershipEventRow>();
    public DbSet<CarrierTelemetrySampleRow> CarrierTelemetrySamples => Set<CarrierTelemetrySampleRow>();
    public DbSet<CarrierDeliveryEvidenceRow> CarrierDeliveryEvidences => Set<CarrierDeliveryEvidenceRow>();
    public DbSet<OrderRow> Orders => Set<OrderRow>();
    public DbSet<OrderLineRow> OrderLines => Set<OrderLineRow>();
    public DbSet<RouteBackgroundJobRow> RouteBackgroundJobs => Set<RouteBackgroundJobRow>();
    public DbSet<RouteSheetRouteCalculationRow> RouteSheetRouteCalculations => Set<RouteSheetRouteCalculationRow>();
    public DbSet<AffiliateRow> Affiliates => Set<AffiliateRow>();
    public DbSet<WarehouseDebtRow> WarehouseDebts => Set<WarehouseDebtRow>();
    public DbSet<AffiliateDebtRow> AffiliateDebts => Set<AffiliateDebtRow>();
    public DbSet<CarrierDebtRow> CarrierDebts => Set<CarrierDebtRow>();
    public DbSet<AnalyticsSessionRow> AnalyticsSessions => Set<AnalyticsSessionRow>();
    public DbSet<AnalyticsPageViewRow> AnalyticsPageViews => Set<AnalyticsPageViewRow>();
    public DbSet<ProductViewEventRow> ProductViewEvents => Set<ProductViewEventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

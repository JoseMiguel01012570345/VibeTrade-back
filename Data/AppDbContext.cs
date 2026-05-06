using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data.Entities;
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
    public DbSet<ChatSocialGroupMemberRow> ChatSocialGroupMembers => Set<ChatSocialGroupMemberRow>();
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
    public DbSet<AgreementCurrencyPaymentRow> AgreementCurrencyPayments => Set<AgreementCurrencyPaymentRow>();
    public DbSet<AgreementRouteLegPaidRow> AgreementRouteLegPaids => Set<AgreementRouteLegPaidRow>();
    public DbSet<AgreementMerchandiseLinePaidRow> AgreementMerchandiseLinePaids => Set<AgreementMerchandiseLinePaidRow>();
    public DbSet<AgreementServicePaymentRow> AgreementServicePayments => Set<AgreementServicePaymentRow>();
    public DbSet<ServiceEvidenceRow> ServiceEvidences => Set<ServiceEvidenceRow>();
    public DbSet<MerchandiseEvidenceRow> MerchandiseEvidences => Set<MerchandiseEvidenceRow>();
    public DbSet<RouteStopDeliveryRow> RouteStopDeliveries => Set<RouteStopDeliveryRow>();
    public DbSet<CarrierOwnershipEventRow> CarrierOwnershipEvents => Set<CarrierOwnershipEventRow>();
    public DbSet<CarrierTelemetrySampleRow> CarrierTelemetrySamples => Set<CarrierTelemetrySampleRow>();
    public DbSet<CarrierDeliveryEvidenceRow> CarrierDeliveryEvidences => Set<CarrierDeliveryEvidenceRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

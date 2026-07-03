using VibeTrade.Backend.Features.Affiliates;
using VibeTrade.Backend.Features.Agreements;
using VibeTrade.Backend.Features.Analytics;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Bootstrap;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Debts;
using VibeTrade.Backend.Features.EmergentOffers;
using VibeTrade.Backend.Features.Health;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Media;
using VibeTrade.Backend.Features.Notifications;
using VibeTrade.Backend.Features.Payments;
using VibeTrade.Backend.Features.Policies;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Routing;
using VibeTrade.Backend.Features.SavedOffers;
using VibeTrade.Backend.Features.Statistics;
using VibeTrade.Backend.Features.Trust;
using VibeTrade.Backend.Features.Users;

namespace VibeTrade.Backend.Infrastructure.Api;

public static class EndpointRouteBuilderExtensions
{
    public static WebApplication MapVibeTradeFeatureEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapSavedOffersEndpoints();
        app.MapAuthEndpoints();
        app.MapBootstrapEndpoints();
        app.MapRecommendationsEndpoints();
        app.MapPoliciesEndpoints();
        app.MapEmergentOffersEndpoints();
        app.MapPaymentsEndpoints();
        app.MapOrdersEndpoints();
        app.MapRouteLogisticsEndpoints();
        app.MapRoutingEndpoints();
        app.MapDebtsEndpoints();
        app.MapUsersEndpoints();
        app.MapAnalyticsEndpoints();
        app.MapStatisticsEndpoints();
        app.MapAffiliatesEndpoints();
        app.MapTrustLedgerEndpoints();
        app.MapMediaEndpoints();
        app.MapLinkPreviewEndpoints();
        app.MapChatNotificationsEndpoints();
        app.MapChatAgreementEvidenceEndpoints();
        app.MapChatAgreementsEndpoints();
        app.MapChatEndpoints();
        app.MapMarketEndpoints();
        return app;
    }
}

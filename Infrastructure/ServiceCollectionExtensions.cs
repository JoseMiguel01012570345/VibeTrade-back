using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using VibeTrade.Backend.Api.Swagger;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Auth.Interfaces;
using VibeTrade.Backend.Features.Bootstrap;
using VibeTrade.Backend.Features.Bootstrap.Interfaces;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.RouteSheets;
using VibeTrade.Backend.Features.Agreements;
using VibeTrade.Backend.Features.Agreements.Interfaces;
using VibeTrade.Backend.Features.Notifications;
using VibeTrade.Backend.Features.Notifications.BroadcastingInterfaces;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.EmergentOffers;
using VibeTrade.Backend.Features.EmergentOffers.Interfaces;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Features.Logistics.Interfaces;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Interfaces;
using VibeTrade.Backend.Features.Payments;
using VibeTrade.Backend.Features.Payments.Interfaces;
using VibeTrade.Backend.Features.Policies.ChatExit;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.Recommendations.Feed;
using VibeTrade.Backend.Features.Recommendations.Guest;
using VibeTrade.Backend.Features.Recommendations.Interfaces;
using VibeTrade.Backend.Features.Routing;
using VibeTrade.Backend.Features.Routing.Interfaces;
using VibeTrade.Backend.Features.SavedOffers;
using VibeTrade.Backend.Features.SavedOffers.Interfaces;
using VibeTrade.Backend.Features.Search.Catalog;
using VibeTrade.Backend.Features.Search.Elasticsearch;
using VibeTrade.Backend.Features.Search.Interfaces;
using VibeTrade.Backend.Features.Trust;
using VibeTrade.Backend.Features.Trust.Interfaces;
using VibeTrade.Backend.Infrastructure;
using VibeTrade.Backend.Infrastructure.Email;
using VibeTrade.Backend.Infrastructure.Email.Interfaces;

namespace VibeTrade.Backend.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVibeTradePersistence(this IServiceCollection services)
    {
        var connectionString = PostgresConfiguration.BuildConnectionString();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>();

        return services;
    }

    public static IServiceCollection AddVibeTradeFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMarketCatalogSyncService, CatalogService>();
        services.AddScoped<IOfferPopularityWeightService, RecommendationService.OfferPopularityWeightService>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<IOfferService, OfferService>();
        services.AddScoped<IMarketWorkspaceService, MarketService>();
        services.AddScoped<IMarketCatalogStoreSearchService, MarketCatalogStoreSearchService>();
        services.AddScoped<IBootstrapService, BootstrapService>();
        services.AddScoped<IGuestBootstrapService, GuestBootstrapService>();
        services.AddScoped<ISavedOffersService, SavedOffersService>();
        services.AddScoped<IEmergentOfferCarrierSubscriptionService, EmergentOfferCarrierSubscriptionService>();
        services.AddScoped<IEmergentRouteTramoSubscriptionRequestService, EmergentRouteTramoSubscriptionRequestService>();
        services.AddScoped<IRecommendationElasticsearchQuery, RecommendationElasticsearchQuery>();
        services.AddScoped<RecommendationFeedV2>();
        services.AddSingleton<IGuestInteractionStore, GuestInteractionStore>();
        services.AddScoped<IGuestRecommendationService, GuestRecommendationService>();
        services.AddScoped<IUserAccountSyncService, UserAccountSyncService>();
        services.AddScoped<IUserContactsService, UserContactsService>();
        services.AddScoped<ITrustScoreLedgerService, TrustScoreLedgerService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<IThreadAccessControlService, ThreadAccessControlService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRouteTramoSubscriptionNotificationService, RouteTramoSubscriptionNotificationService>();
        services.AddScoped<IRouteSheetThreadNotificationService, RouteSheetThreadNotificationService>();
        services.AddScoped<IBroadcastingService, BroadcastingService>();
        services.AddScoped<ISignalRBroadcastService>(sp => sp.GetRequiredService<IBroadcastingService>());
        services.AddScoped<IChatMessageInserter>(sp => sp.GetRequiredService<ChatService>());
        services.AddScoped<IChatThreadSystemMessageService>(sp =>
        {
            var lazyInserter = new Lazy<IChatMessageInserter>(() => sp.GetRequiredService<ChatService>());
            return new ChatThreadSystemMessageService(
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<IThreadAccessControlService>(),
                lazyInserter);
        });
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IThreadManagementService, ChatService>();
        services.AddScoped<IMessageHandlingService, ChatService>();
        services.AddScoped<IParticipantManagementService, ChatService>();
        services.AddScoped<IOfferRelationService, ChatService>();
        services.AddSingleton<IChatExitPolicyRegistry, ChatExitPolicyRegistry>();
        services.AddScoped<IChatExitOperationsService, ChatExitOperationsService>();
        services.AddScoped<IPartySoftLeaveCoordinator, PartySoftLeaveCoordinator>();
        services.AddScoped<IRouteSheetChatService, RouteSheetChatService>();
        services.AddScoped<IRouteTramoSubscriptionService, RouteTramoSubscriptionService>();
        services.AddScoped<ITradeAgreementService, TradeAgreementService>();
        services.AddScoped<IPaymentsService, PaymentsService>();
        services.AddScoped<IStripeUserPaymentService, PaymentsService>();
        services.AddScoped<IStripePaymentIntentService, PaymentsService>();
        services.AddScoped<IAgreementPaymentService, PaymentsService>();
        services.AddScoped<ICarrierTelemetryService, CarrierTelemetryService>();
        services.AddScoped<ICarrierOwnershipService, CarrierOwnershipService>();
        services.AddScoped<ICarrierDeliveryEvidenceService, CarrierDeliveryEvidenceService>();
        services.AddScoped<ICarrierLegRefundService, CarrierLegRefundService>();
        services.AddHostedService<CarrierEvidenceDeadlineWatcher>();
        services.AddScoped<IAgreementServiceEvidenceService, AgreementServiceEvidenceService>();
        services.AddScoped<IAgreementMerchandiseEvidenceService, AgreementMerchandiseEvidenceService>();
        services.Configure<EmailSmtpOptions>(
            configuration.GetSection(EmailSmtpOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IPaymentFeeReceiptEmailDispatcher, PaymentFeeReceiptEmailDispatcher>();
        services.Configure<RoutingOptions>(
            configuration.GetSection(RoutingOptions.SectionName));
        services.AddHttpClient<IDrivingLegRoutingService, GraphHopperDrivingLegService>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeTradeBackend/1.0");
            var opt = sp.GetRequiredService<IOptions<RoutingOptions>>().Value;
            var baseUrl = (opt.GraphHopperBaseUrl ?? "").Trim();
            if (baseUrl.Length == 0)
                return;
            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                client.BaseAddress = uri;
        });
        services.AddHttpClient("linkPreview", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(8);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("VibeTradeLinkPreview/1.0");
        });
        services.AddMemoryCache();

        services.Configure<ElasticsearchStoreSearchOptions>(
            configuration.GetSection(ElasticsearchStoreSearchOptions.SectionName));
        services.AddSingleton<IStoreSearchTextEmbeddingService, StoreSearchMlNetTfIdfEmbeddingService>();
        services.AddScoped<IElasticsearchStoreSearchQuery, ElasticsearchStoreSearchQuery>();
        services.AddScoped<IStoreSearchIndexWriter, ElasticsearchStoreSearchIndexWriter>();
        services.AddHostedService<ElasticsearchSearchStartupHostedService>();
        services.AddHostedService<ElasticsearchDailyReindexHostedService>();

        return services;
    }

    public static IServiceCollection AddVibeTradeApi(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        services.AddSignalR();
        services.AddEndpointsApiExplorer();
        services.AddVibeTradeSwagger();
        services.AddVibeTradeCors();

        return services;
    }

    private static IServiceCollection AddVibeTradeSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "VibeTrade API",
                Version = "v1",
                Description =
                    "### Visión general\n"
                    + "API REST usada por el cliente web VibeTrade: mercado, autenticación por OTP, chat, recomendaciones y medios.\n\n"
                    + "### Cabeceras\n"
                    + "- **`Authorization`**: `Bearer {token}` tras `POST /api/v1/auth/verify` (sesión opaca almacenada en servidor).\n\n"
                    + "### Salud y entorno\n"
                    + "- `GET /health` — JSON; **503** si PostgreSQL u otra dependencia falla.\n"
                    + "- Swagger UI local: `http://localhost:5110/swagger` (puerto HTTP por defecto del backend).\n\n"
                    + "### Convenciones\n"
                    + "- Rutas bajo prefijo `api/v1/` salvo `GET /health`.\n"
                    + "- Cuerpos JSON en **camelCase** (configuración ASP.NET Core).",
                Contact = new OpenApiContact
                {
                    Name = "VibeTrade",
                },
            });

            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description =
                    "Token de sesión devuelto por `POST /api/v1/auth/verify` en el campo `sessionToken`. "
                    + "Usar el botón **Authorize** y el esquema `Bearer` para probar rutas que requieren sesión.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "opaque",
            });

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml))
                o.IncludeXmlComments(xml, includeControllerXmlComments: true);
            o.DocumentFilter<TagDescriptionsDocumentFilter>();
        });

        return services;
    }

    private static IServiceCollection AddVibeTradeCors(this IServiceCollection services)
    {
        services.AddCors(o =>
        {
            o.AddPolicy("Dev", p =>
                p.SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
        });

        return services;
    }
}

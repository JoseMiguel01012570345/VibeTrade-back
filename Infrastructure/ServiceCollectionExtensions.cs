using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi.Models;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Agreements;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Bootstrap;
using VibeTrade.Backend.Features.Catalog;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.EmergentOffers;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Offers;
using VibeTrade.Backend.Features.Notifications;
using VibeTrade.Backend.Features.Payments;
using VibeTrade.Backend.Features.Policies;
using VibeTrade.Backend.Features.Recommendations;
using VibeTrade.Backend.Features.RouteSheets;
using VibeTrade.Backend.Features.RouteTramoSubscriptions;
using VibeTrade.Backend.Features.Routing;
using VibeTrade.Backend.Features.SavedOffers;
using VibeTrade.Backend.Features.Search;
using VibeTrade.Backend.Features.Trust;
using VibeTrade.Backend.Infrastructure.Swagger;

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
        services
            .AddInfrastructureFeature(configuration)
            .AddCatalogFeature()
            .AddOffersFeature()
            .AddMarketFeature()
            .AddSearchFeature(configuration)
            .AddRecommendationsFeature()
            .AddBootstrapFeature()
            .AddSavedOffersFeature()
            .AddEmergentOffersFeature()
            .AddAuthFeature()
            .AddTrustFeature()
            .AddChatFeature()
            .AddNotificationsFeature()
            .AddPoliciesFeature()
            .AddRouteSheetsFeature()
            .AddRouteTramoSubscriptionsFeature()
            .AddAgreementsFeature()
            .AddPaymentsFeature()
            .AddLogisticsFeature()
            .AddRoutingFeature(configuration);

        return services;
    }

    public static IServiceCollection AddVibeTradeApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
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
                o.IncludeXmlComments(xml);
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

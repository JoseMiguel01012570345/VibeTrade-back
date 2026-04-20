using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace VibeTrade.Backend.Api.Swagger;

/// <summary>
/// Añade descripciones a los tags de OpenAPI para que Swagger UI muestre ayuda por grupo de endpoints.
/// </summary>
public sealed class TagDescriptionsDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Tags =
        [
            new OpenApiTag
            {
                Name = "Health",
                Description = "Estado del proceso y dependencias (PostgreSQL vía health checks).",
            },
            new OpenApiTag
            {
                Name = "Bootstrap",
                Description = "Carga inicial del cliente: mercado, recomendaciones, ofertas guardadas y (si hay sesión) hilos de chat.",
            },
            new OpenApiTag
            {
                Name = "Auth",
                Description = "OTP por teléfono, sesión Bearer, perfil y contactos. `Auth:ExposeDevCodes` puede exponer códigos de prueba en desarrollo.",
            },
            new OpenApiTag
            {
                Name = "Market",
                Description = "Workspace JSON, tiendas, catálogo, búsqueda (Elasticsearch), consultas públicas (QA), likes y detalle de tienda.",
            },
            new OpenApiTag
            {
                Name = "Chat",
                Description = "Hilos de compra/venta por oferta, mensajes y estado de entrega.",
            },
            new OpenApiTag
            {
                Name = "Recommendations",
                Description = "Feed de recomendaciones e interacciones (usuario o invitado con `guestId`).",
            },
            new OpenApiTag
            {
                Name = "Notifications",
                Description = "Notificaciones in-app del chat (listar y marcar leídas).",
            },
            new OpenApiTag
            {
                Name = "Media",
                Description = "Subida multipart (máx. 5 MB por archivo en POST) y descarga por id.",
            },
            new OpenApiTag
            {
                Name = "Saved offers",
                Description = "Ids de ofertas guardadas en el perfil del usuario autenticado.",
            },
        ];
    }
}

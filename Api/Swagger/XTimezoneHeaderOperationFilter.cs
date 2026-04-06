using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using VibeTrade.Backend.Utils.TimeZone;

namespace VibeTrade.Backend.Api.Swagger;

/// <summary>Documenta la cabecera <see cref="TimeZoneHeaderMiddleware.HeaderName"/> en cada operación.</summary>
public sealed class XTimezoneHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();
        if (operation.Parameters.Any(p => p.Name == TimeZoneHeaderMiddleware.HeaderName && p.In == ParameterLocation.Header))
            return;

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = TimeZoneHeaderMiddleware.HeaderName,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Zona horaria IANA del cliente (p. ej. `America/Argentina/Buenos_Aires`). Recomendada en todas las peticiones (flow-ui).",
            Schema = new OpenApiSchema { Type = "string", Example = new Microsoft.OpenApi.Any.OpenApiString("America/Argentina/Buenos_Aires") },
        });
    }
}

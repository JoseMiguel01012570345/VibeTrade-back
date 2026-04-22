namespace VibeTrade.Backend.Data.RouteSheets;

/// <summary>Resumen tipado de una hoja publicada para señales de recomendación (sin duplicar el payload completo).</summary>
public sealed class EmergentRouteLegSnapshot
{
    public string Origen { get; set; } = "";

    public string Destino { get; set; } = "";
}

public sealed class EmergentRouteSheetSnapshot
{
    public string Titulo { get; set; } = "";

    public string MercanciasResumen { get; set; } = "";

    public List<EmergentRouteLegSnapshot> Paradas { get; set; } = new();

    public static EmergentRouteSheetSnapshot FromRouteSheet(RouteSheetPayload sheet) => new()
    {
        Titulo = sheet.Titulo ?? "",
        MercanciasResumen = sheet.MercanciasResumen ?? "",
        Paradas = (sheet.Paradas ?? [])
            .Select(p => new EmergentRouteLegSnapshot
            {
                Origen = p.Origen ?? "",
                Destino = p.Destino ?? "",
            })
            .ToList(),
    };
}

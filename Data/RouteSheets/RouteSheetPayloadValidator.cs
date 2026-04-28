using System.Globalization;
using System.Text.RegularExpressions;

namespace VibeTrade.Backend.Data.RouteSheets;

/// <summary>
/// Validación server-side alineada con la hoja de ruta en el cliente (tiempos estimados por tramo).
/// </summary>
public static class RouteSheetPayloadValidator
{
    /// <summary>Mismo patrón que <c>ROUTE_ESTIMADO_ISO_LOCAL_RE</c> en el front: <c>YYYY-MM-DDTHH:mm</c>.</summary>
    private static readonly Regex EstimadoIsoLocal = new(
        @"^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})$",
        RegexOptions.Compiled);

    /// <summary>
    /// Devuelve mensaje de error si el payload no debe persistirse; <c>null</c> si está permitido.
    /// Construye sub-rutas (cadenas) por coincidencia <c>destino[i]</c> ↔ <c>origen[i+1]</c> y valida
    /// dentro de cada cadena: la entrega estimada del tramo previo no puede ser posterior a la recogida
    /// estimada del tramo siguiente cuando ambas están en ISO completo.
    /// </summary>
    public static string? Validate(RouteSheetPayload payload)
    {
        var paradas = payload.Paradas ?? [];
        var chains = BuildTramoChainsByCoords(paradas);
        foreach (var chain in chains)
        {
            for (var k = 0; k < chain.Count - 1; k++)
            {
                var a = chain[k];
                var b = chain[k + 1];
                if (!TryParseEstimadoIsoLocal(paradas[a].TiempoEntregaEstimado, out var entrega))
                    continue;
                if (!TryParseEstimadoIsoLocal(paradas[b].TiempoRecogidaEstimado, out var recogidaSiguiente))
                    continue;
                if (entrega > recogidaSiguiente)
                    return "La entrega estimada no puede ser posterior a la recogida estimada del tramo siguiente.";
            }
        }

        return null;
    }

    /// <summary>
    /// Lista de listas con índices 0-based de tramos. Dos tramos consecutivos quedan en la misma cadena
    /// si el origen del segundo coincide con el destino del primero (lat/lng tras <c>trim</c>); si difieren, abre cadena nueva.
    /// </summary>
    private static List<List<int>> BuildTramoChainsByCoords(IReadOnlyList<RouteStopPayload> paradas)
    {
        var chains = new List<List<int>>();
        if (paradas.Count == 0)
            return chains;
        var current = new List<int> { 0 };
        for (var i = 1; i < paradas.Count; i++)
        {
            if (OrigenCoincideConDestinoAnterior(paradas[i - 1], paradas[i]))
            {
                current.Add(i);
            }
            else
            {
                chains.Add(current);
                current = [i];
            }
        }
        chains.Add(current);
        return chains;
    }

    /// <summary>
    /// Tras expandir cadenas en el cliente, los tramos enlazados comparten coordenadas destino→origen;
    /// si el usuario eligió otro origen en mapa, estas coordenadas difieren (ruta nueva).
    /// </summary>
    private static bool OrigenCoincideConDestinoAnterior(RouteStopPayload anterior, RouteStopPayload siguiente)
    {
        var dLat = (anterior.DestinoLat ?? "").Trim();
        var dLng = (anterior.DestinoLng ?? "").Trim();
        var oLat = (siguiente.OrigenLat ?? "").Trim();
        var oLng = (siguiente.OrigenLng ?? "").Trim();
        if (dLat.Length == 0 || dLng.Length == 0 || oLat.Length == 0 || oLng.Length == 0)
            return false;
        return string.Equals(dLat, oLat, StringComparison.Ordinal)
            && string.Equals(dLng, oLng, StringComparison.Ordinal);
    }

    private static bool TryParseEstimadoIsoLocal(string? raw, out DateTime local)
    {
        local = default;
        var t = (raw ?? "").Trim();
        var m = EstimadoIsoLocal.Match(t);
        if (!m.Success)
            return false;
        return DateTime.TryParseExact(
            $"{m.Groups[1].Value}T{m.Groups[2].Value}:00",
            "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out local);
    }
}

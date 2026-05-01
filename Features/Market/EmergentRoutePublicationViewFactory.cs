using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Data.RouteSheets;
using VibeTrade.Backend.Domain.Market;

namespace VibeTrade.Backend.Features.Market;

internal static class EmergentRoutePublicationViewFactory
{
    public static HomeOfferViewDto Create(
        EmergentOfferRow e,
        StoreProductRow? p,
        StoreServiceRow? s,
        string? fallbackStoreId,
        RouteSheetPayload? liveRoutePayload = null)
    {
        var node = CreateCore(e, p, s, fallbackStoreId);
        if (liveRoutePayload is not null)
            ApplyLiveParadaStopIds(node, liveRoutePayload);
        return node;
    }

    private static HomeOfferViewDto CreateCore(
        EmergentOfferRow e,
        StoreProductRow? p,
        StoreServiceRow? s,
        string? fallbackStoreId)
    {
        var snap = e.RouteSheetSnapshot ?? new EmergentRouteSheetSnapshot();
        var storeIdForOrphan = (fallbackStoreId ?? "").Trim();
        var baseH = p is not null
            ? HomeOfferViewFactory.FromProductRow(p)
            : s is not null
                ? HomeOfferViewFactory.FromServiceRow(s)
                : new HomeOfferViewDto
                {
                    Id = e.Id,
                    Title = snap.Titulo,
                    StoreId = string.IsNullOrEmpty(storeIdForOrphan) ? "" : storeIdForOrphan,
                    Price = "—",
                };

        baseH.Id = e.Id;
        baseH.EmergentBaseOfferId = e.OfferId;
        baseH.EmergentThreadId = e.ThreadId;
        baseH.EmergentRouteSheetId = e.RouteSheetId;
        baseH.IsEmergentRoutePublication = true;
        if (!string.IsNullOrWhiteSpace(snap.MonedaPago))
            baseH.EmergentMonedaPago = snap.MonedaPago.Trim();
        if (!string.IsNullOrWhiteSpace(snap.Titulo))
            baseH.Title = snap.Titulo.Trim();
        var routeLine = RouteSummaryLine(snap);
        var prevDesc = baseH.Description ?? "";
        if (!string.IsNullOrWhiteSpace(routeLine))
        {
            baseH.Description = string.IsNullOrWhiteSpace(prevDesc)
                ? routeLine
                : routeLine + "\n\n" + prevDesc;
        }

        baseH.Tags.Add("Hoja de ruta (publicada)");

        var paradasSnap = snap.Paradas ?? [];
        if (paradasSnap.Count > 0)
        {
            var paradas = new List<EmergentRouteParadaViewDto>();
            var idx = 0;
            foreach (var leg in paradasSnap)
            {
                idx++;
                var orden = leg.Orden > 0 ? leg.Orden : idx;
                var v = new EmergentRouteParadaViewDto
                {
                    Origen = leg.Origen?.Trim() ?? "",
                    Destino = leg.Destino?.Trim() ?? "",
                    Orden = orden,
                };
                var sid = (leg.StopId ?? "").Trim();
                if (sid.Length > 0)
                    v.StopId = sid;
                if (!string.IsNullOrWhiteSpace(leg.OrigenLat)) v.OrigenLat = leg.OrigenLat!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.OrigenLng)) v.OrigenLng = leg.OrigenLng!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.DestinoLat)) v.DestinoLat = leg.DestinoLat!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.DestinoLng)) v.DestinoLng = leg.DestinoLng!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.MonedaPago)) v.MonedaPago = leg.MonedaPago.Trim();
                if (!string.IsNullOrWhiteSpace(leg.PrecioTransportista))
                    v.PrecioTransportista = leg.PrecioTransportista.Trim();
                if (leg.OsrmRoadKm is double km && km >= 0 && !double.IsNaN(km) && !double.IsInfinity(km))
                    v.OsrmRoadKm = km;
                if (leg.OsrmRouteLatLngs is { Count: >= 2 })
                    v.OsrmRouteLatLngs = leg.OsrmRouteLatLngs;
                paradas.Add(v);
            }

            baseH.EmergentRouteParadas = paradas;
        }

        baseH.Qa = e.OfferQa ?? new List<OfferQaComment>();
        return baseH;
    }

    public static void ApplyLiveParadaStopIds(HomeOfferViewDto offer, RouteSheetPayload payload)
    {
        if (offer.EmergentRouteParadas is not { Count: > 0 } arr)
            return;

        var live = (payload.Paradas ?? [])
            .OrderBy(p => p.Orden)
            .ToList();
        if (live.Count == 0)
            return;

        foreach (var legNode in arr)
        {
            RouteStopPayload? match = null;
            if (legNode.Orden > 0)
            {
                match = live.FirstOrDefault(p => p.Orden == legNode.Orden);
            }

            var sid = (match?.Id ?? "").Trim();
            if (sid.Length > 0)
                legNode.StopId = sid;
            if (match?.OsrmRoadKm is double kmLive && kmLive >= 0 && !double.IsNaN(kmLive) && !double.IsInfinity(kmLive))
                legNode.OsrmRoadKm = kmLive;
            if (match?.OsrmRouteLatLngs is { Count: >= 2 })
                legNode.OsrmRouteLatLngs = match.OsrmRouteLatLngs;
        }
    }

    private static string RouteSummaryLine(EmergentRouteSheetSnapshot snap)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(snap.MercanciasResumen))
            parts.Add(snap.MercanciasResumen.Trim());
        foreach (var leg in snap.Paradas ?? [])
        {
            if (!string.IsNullOrWhiteSpace(leg.Origen) && !string.IsNullOrWhiteSpace(leg.Destino))
                parts.Add($"{leg.Origen} → {leg.Destino}");
        }
        return string.Join(" · ", parts);
    }
}

using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

/// <summary>Clave normalizada para operaciones del vendedor sobre tramos de una hoja.</summary>
public readonly record struct SellerTramoKey(
    string ActorId,
    string ThreadId,
    string RouteSheetId,
    string CarrierId,
    string StopRestrict)
{
    public static SellerTramoKey FromAction(TramoSellerSheetAction a) =>
        new(
            (a.ActorUserId ?? "").Trim(),
            (a.ThreadId ?? "").Trim(),
            (a.RouteSheetId ?? "").Trim(),
            (a.CarrierUserId ?? "").Trim(),
            (a.StopId ?? "").Trim());
}

/// <summary>Contexto preparado para expulsar transportista (persistencia + efectos laterales).</summary>
public sealed record SellerExpelContext(
    ChatThreadRow Thread,
    string SellerUserId,
    string ThreadId,
    string CarrierUserId,
    string ReasonTrim,
    bool ExpelSingleTramo,
    List<RouteTramoSubscriptionRow> Subs,
    int ConfirmedStopsWithdrawnCount,
    bool CarrierFullyRemovedFromThread,
    bool ApplyStoreTrustPenalty,
    IReadOnlyList<string> DistinctRouteSheetIds);

/// <summary>Datos cargados para flujo presel (transportista + hoja).</summary>
public sealed record PreselCore(
    ChatThreadRow Thread,
    UserAccount Carrier,
    RouteSheetPayload Payload,
    string Rsid);

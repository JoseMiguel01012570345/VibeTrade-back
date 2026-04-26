namespace VibeTrade.Backend.Features.Chat;

/// <summary>Entrada al registrar una solicitud de tramo (varios parámetros de dominio).</summary>
public sealed record RecordRouteTramoSubscriptionRequestArgs(
    string ThreadId,
    string RouteSheetId,
    string StopId,
    int StopOrden,
    string CarrierUserId,
    string? StoreServiceId,
    string TransportServiceLabel,
    string? CarrierContactPhone = null);

/// <summary>Acción del vendedor sobre solicitudes de transporte (aceptar / rechazar tramos).</summary>
public sealed record TramoSellerSheetAction(
    string ActorUserId,
    string ThreadId,
    string RouteSheetId,
    string CarrierUserId,
    string? StopId = null);

/// <summary>Transportista: aceptar o rechazar invitación por teléfono en hoja de ruta.</summary>
public sealed record CarrierPreselInviteRequest(
    string CarrierUserId,
    string ThreadId,
    string RouteSheetId,
    string? StopIdRestrict,
    bool Accepted);

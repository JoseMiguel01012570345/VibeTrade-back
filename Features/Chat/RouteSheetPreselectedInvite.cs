namespace VibeTrade.Backend.Features.Chat;

/// <summary>
/// Un tramo concreto cuyo teléfono de transportista cambió al guardar la hoja; destino del aviso presel.
/// </summary>
public sealed record RouteSheetPreselectedInvite(string StopId, string Phone);

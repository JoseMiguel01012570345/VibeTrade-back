using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Features.Logistics;

/// <summary>
/// Grafo de estados de <see cref="RouteStopDeliveryRow.State"/> (tramo de hoja de ruta).
/// Documenta transiciones directas; el backend puede aplicar ramas según contexto (p. ej. primer tramo pagado → <see cref="RouteStopDeliveryStates.InTransit"/>).
/// </summary>
public static class RouteStopDeliveryStateGraph
{
    /// <summary>Transiciones directas posibles desde <paramref name="state"/> (sin ramificar por contexto externo).</summary>
    public static IReadOnlyList<string> NextStates(string state)
    {
        var s = Normalize(state);
        return s switch
        {
            RouteStopDeliveryStates.Unpaid => [RouteStopDeliveryStates.Paid],
            RouteStopDeliveryStates.Paid =>
            [
                RouteStopDeliveryStates.AwaitingCarrierForHandoff,
                RouteStopDeliveryStates.InTransit,
            ],
            RouteStopDeliveryStates.AwaitingCarrierForHandoff => [RouteStopDeliveryStates.InTransit],
            RouteStopDeliveryStates.InTransit =>
            [
                RouteStopDeliveryStates.DeliveredPendingEvidence,
                RouteStopDeliveryStates.IdleStoreCustody,
            ],
            RouteStopDeliveryStates.IdleStoreCustody => [RouteStopDeliveryStates.InTransit],
            RouteStopDeliveryStates.DeliveredPendingEvidence =>
            [
                RouteStopDeliveryStates.EvidenceSubmitExpired,
                RouteStopDeliveryStates.EvidenceSubmitted,
            ],
            RouteStopDeliveryStates.EvidenceSubmitExpired => [RouteStopDeliveryStates.Refunded],
            RouteStopDeliveryStates.EvidenceSubmitted =>
            [
                RouteStopDeliveryStates.EvidenceAccepted,
                RouteStopDeliveryStates.EvidenceRejected,
            ],
            RouteStopDeliveryStates.EvidenceRejected =>
            [
                RouteStopDeliveryStates.EvidenceSubmitted,
                RouteStopDeliveryStates.Refunded,
            ],
            RouteStopDeliveryStates.EvidenceAccepted => [],
            RouteStopDeliveryStates.Refunded => [],
            _ => [],
        };
    }

    /// <summary>
    /// Indica si <paramref name="myState"/> es un sucesor directo de <paramref name="inspectState"/>
    /// según <see cref="NextStates"/>.
    /// </summary>
    public static bool Contains(string inspectState, string myState) =>
        NextStates(inspectState).Contains(Normalize(myState), StringComparer.Ordinal);

    /// <summary>
    /// Tramo apto para recibir POST de telemetría: excluye no pagado, cierre por evidencia aceptada, reembolsos y vencimiento de plazo de evidencia.
    /// (Corrige la intención lógica de <c>!= A || != B</c> → cadena de <c>!=</c> con AND.)
    /// </summary>
    public static bool Active(string state)
    {
        var s = Normalize(state);
        if (s.Length == 0)
            return false;
        return s != RouteStopDeliveryStates.Unpaid
            && s != RouteStopDeliveryStates.EvidenceAccepted
            && s != RouteStopDeliveryStates.IdleStoreCustody
            && !RouteStopDeliveryStates.IsRefundedTerminal(s)
            && s != RouteStopDeliveryStates.EvidenceSubmitExpired;
    }

    private static string Normalize(string? state) => (state ?? "").Trim();
}

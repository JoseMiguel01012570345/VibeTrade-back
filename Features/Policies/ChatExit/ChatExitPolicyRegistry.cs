using Microsoft.AspNetCore.Http;

namespace VibeTrade.Backend.Features.Policies.ChatExit;

/// <inheritdoc />
public sealed class ChatExitPolicyRegistry : IChatExitPolicyRegistry
{
    private static readonly ChatExitPolicyDefinition[] All =
    [
        new(
            "reason_required",
            "party",
            StatusCodes.Status400BadRequest,
            "Indica un motivo para salir.",
            "Motivo obligatorio en party-soft-leave."),
        new(
            "not_eligible_party",
            "party",
            StatusCodes.Status403Forbidden,
            "Tu usuario no es el comprador ni el vendedor registrados en este hilo (por ejemplo, sos transportista). Esta acción es la salida «con acuerdo» del comprador/vendedor: para abandonar los tramos como transportista usá Salir del chat desde la lista; el sistema des-suscribe los tramos automáticamente.",
            "Solo comprador o vendedor del hilo pueden usar la salida con acuerdo."),
        new(
            "party_leave_no_accepted_agreement",
            "party",
            StatusCodes.Status409Conflict,
            "No hay acuerdos aceptados en este hilo: no corresponde la salida con acuerdo. Podés quitar el chat de tu lista sin este paso.",
            "Requiere al menos un acuerdo aceptado."),
        new(
            "party_leave_thread_not_found",
            "party",
            StatusCodes.Status404NotFound,
            "El chat no existe o ya no está disponible.",
            "Hilo inexistente o no disponible."),
        new(
            "party_leave_invalid_request",
            "party",
            StatusCodes.Status400BadRequest,
            "Solicitud de salida inválida.",
            "Petición inválida."),
        new(
            "party_leave_notice_failed",
            "party",
            StatusCodes.Status400BadRequest,
            "No se pudo registrar el aviso de salida. Reintentá en unos segundos.",
            "Fallo al persistir el aviso de salida."),
        new(
            "held_payments_buyer",
            "party",
            StatusCodes.Status409Conflict,
            "No podés salir del chat mientras haya pagos retenidos (servicios y/o mercadería en espera). Esperá la liberación o el reembolso.",
            "Pagos retenidos (comprador)."),
        new(
            "held_payments_seller_mixed",
            "party",
            StatusCodes.Status409Conflict,
            "No podés salir del chat con pagos retenidos cuando el acuerdo mezcla servicios y mercadería. Coordiná la liberación o el reembolso con la contraparte.",
            "Acuerdo mixto servicio+mercadería con pagos retenidos."),
        new(
            "evidence_pending",
            "party",
            StatusCodes.Status409Conflict,
            "No podés salir del chat mientras haya evidencia enviada al comprador sin resolver o rechazada con pago aún retenido (servicio o mercadería).",
            "Evidencia pendiente o rechazada con pago retenido."),
        new(
            "route_delivery_active_buyer",
            "party",
            StatusCodes.Status409Conflict,
            "No podés salir del chat mientras haya entregas de ruta activas en este acuerdo (tramos pagados / en curso). Esperá la evidencia o solicitá reembolso elegible.",
            "Entrega de ruta activa (comprador)."),
        new(
            "route_delivery_active_seller",
            "party",
            StatusCodes.Status409Conflict,
            "No podés salir del chat mientras haya entregas de ruta activas en este acuerdo (tramos pagados / en curso). Coordiná la evidencia o el reembolso elegible con la contraparte.",
            "Entrega de ruta activa (vendedor)."),
        new(
            "stripe_refund_failed",
            "party",
            StatusCodes.Status502BadGateway,
            "No se pudieron reembolsar los pagos retenidos en este momento. Reintentá en unos minutos o contactá soporte.",
            "Fallo de reembolso Stripe al salir el vendedor."),
        new(
            "carrier_holds_ownership",
            "carrier",
            StatusCodes.Status409Conflict,
            "No podés salir mientras tengas carga asignada como transportista activo.",
            "Titularidad operativa de tramo."),
        new(
            "carrier_route_evidence_rejected",
            "carrier",
            StatusCodes.Status409Conflict,
            "Tenés evidencia de tramo rechazada: reenviá o coordiná con la tienda antes de salir de la operación.",
            "Evidencia de ruta rechazada."),
        new(
            "carrier_route_post_cede_pending",
            "carrier",
            StatusCodes.Status409Conflict,
            "Cediste la titularidad en un tramo: no puedes salir hasta que la evidencia esté aceptada o el tramo reembolsado.",
            "Post-cesión: evidencia o reembolso pendiente."),
    ];

    private static readonly Dictionary<string, ChatExitPolicyDefinition> PartyByCode = All
        .Where(x => string.Equals(x.Audience, "party", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ChatExitPolicyDefinition> CarrierByCode = All
        .Where(x => string.Equals(x.Audience, "carrier", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

    public bool TryMapPartySoftLeaveFailure(string? errorCode, out int statusCode, out string message)
    {
        statusCode = 0;
        message = "";
        if (string.IsNullOrWhiteSpace(errorCode) || !PartyByCode.TryGetValue(errorCode, out var def))
            return false;
        statusCode = def.HttpStatus;
        message = def.MessageEs;
        return true;
    }

    public bool TryMapCarrierWithdrawFailure(string? errorCode, out int statusCode, out string message)
    {
        statusCode = 0;
        message = "";
        if (string.IsNullOrWhiteSpace(errorCode) || !CarrierByCode.TryGetValue(errorCode, out var def))
            return false;
        statusCode = def.HttpStatus;
        message = def.MessageEs;
        return true;
    }
}

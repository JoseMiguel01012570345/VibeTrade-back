namespace VibeTrade.Backend.Features.RouteSheets.Dtos;

/// <summary>Resultado de mutaciones de hoja de ruta (PUT/DELETE).</summary>
public enum RouteSheetMutationResult
{
    Ok,
    NotFoundOrForbidden,
    /// <summary>Hoja vinculada a un acuerdo con cobros exitosos.</summary>
    LockedByPaidAgreement,
    /// <summary>Ya existen tantas hojas activas como acuerdos aceptados sin cobro exitoso; no se puede crear una más.</summary>
    ExceedsUnpaidAgreementLimit,
    /// <summary>Tramos con moneda distinta a la mercadería del acuerdo vinculado.</summary>
    RouteCurrencyMerchandiseMismatch,
    /// <summary>No se puede publicar una hoja ya marcada como entregada.</summary>
    CannotPublishDeliveredSheet,
    /// <summary>No se puede publicar en plataforma sin vincular la hoja a un acuerdo.</summary>
    CannotPublishWithoutAgreementLink,
}

namespace VibeTrade.Backend.Features.RouteSheets.Dtos;

/// <summary>Resultado de mutaciones de hoja de ruta (PUT/DELETE).</summary>
public enum RouteSheetMutationResult
{
    Ok,
    NotFoundOrForbidden,
    /// <summary>Hoja vinculada a un acuerdo con cobros Stripe exitosos.</summary>
    LockedByPaidAgreement,
    /// <summary>Ya existen tantas hojas activas como acuerdos aceptados sin cobro exitoso; no se puede crear una más.</summary>
    ExceedsUnpaidAgreementLimit,
    /// <summary>Tramos con moneda distinta a la mercadería del acuerdo vinculado.</summary>
    RouteCurrencyMerchandiseMismatch,
}

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Resultado de mutaciones de hoja de ruta (PUT/DELETE).</summary>
public enum RouteSheetMutationResult
{
    Ok,
    NotFoundOrForbidden,
    /// <summary>Hoja vinculada a un acuerdo con cobros Stripe exitosos.</summary>
    LockedByPaidAgreement,
}

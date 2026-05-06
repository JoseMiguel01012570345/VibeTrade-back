namespace VibeTrade.Backend.Features.Chat.RouteSheets;

/// <summary>Resultado de mutaciones de hoja de ruta (PUT/DELETE).</summary>
public enum RouteSheetMutationResult
{
    Ok,
    NotFoundOrForbidden,
    /// <summary>Hoja vinculada a un acuerdo con cobros Stripe exitosos.</summary>
    LockedByPaidAgreement,
    /// <summary>No se puede publicar hasta que un acuerdo del hilo tenga <c>RouteSheetId</c> apuntando a esta hoja.</summary>
    PublishRequiresAgreementLink,
}

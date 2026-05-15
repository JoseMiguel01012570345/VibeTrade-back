namespace VibeTrade.Backend.Features.RouteTramoSubscriptions.Dtos;

/// <summary>Resultado de retiro del transportista del hilo.</summary>
public sealed record CarrierWithdrawFromThreadResult(
    int WithdrawnRowCount,
    bool ApplyTrustPenalty,
    int? TrustScoreAfterPenalty = null)
{
    /// <summary>P.ej. <c>carrier_holds_ownership</c> cuando el transportista tiene carga asignada.</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>Resultado de expulsión de transportista por el vendedor.</summary>
public sealed record CarrierExpelledBySellerResult(
    int WithdrawnRowCount,
    bool ApplyStoreTrustPenalty,
    int? StoreTrustScoreAfter = null,
    int ConfirmedStopsWithdrawnCount = 0,
    bool CarrierFullyRemovedFromThread = false);

public sealed record RouteTramoSubscriptionItemDto(
    string RouteSheetId,
    string StopId,
    int Orden,
    string CarrierUserId,
    string DisplayName,
    string Phone,
    int TrustScore,
    string? StoreServiceId,
    string TransportServiceLabel,
    string Status,
    string OrigenLine,
    string DestinoLine,
    long CreatedAtUnixMs,
    string? CarrierServiceStoreId,
    string? CarrierAvatarUrl);

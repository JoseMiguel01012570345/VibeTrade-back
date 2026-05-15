namespace VibeTrade.Backend.Features.Agreements.Dtos;

/// <summary>Resultado de <see cref="VibeTrade.Backend.Features.Agreements.Interfaces.ITradeAgreementService.SetRouteSheetLinkAsync"/>.</summary>
public sealed record TradeAgreementRouteSheetLinkOutcome(
    TradeAgreementApiResponse? Response,
    int? FailureStatusCode,
    string? FailureMessage);

namespace VibeTrade.Backend.Features.Chat;

/// <summary>Resultado de <see cref="ITradeAgreementService.SetRouteSheetLinkAsync"/>.</summary>
public sealed record TradeAgreementRouteSheetLinkOutcome(
    TradeAgreementApiResponse? Response,
    int? FailureStatusCode,
    string? FailureMessage);

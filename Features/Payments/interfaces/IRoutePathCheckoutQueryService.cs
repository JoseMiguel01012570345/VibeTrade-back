using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Payments.Interfaces;

public interface IRoutePathCheckoutQueryService
{
  Task<AgreementRoutePathsDto?> GetAgreementRoutePathsAsync(
    string userId,
    string threadId,
    string agreementId,
    string routeSheetId,
    CancellationToken cancellationToken = default);
}

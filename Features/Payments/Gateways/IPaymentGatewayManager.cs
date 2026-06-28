using VibeTrade.Backend.Features.Payments.Dtos;

namespace VibeTrade.Backend.Features.Payments.Gateways;

public interface IPaymentGatewayManager
{
    PaymentGatewayBase GetGateway(string? gatewayId = null);

    IReadOnlyList<string> RegisteredGatewayIds { get; }

    PaymentGatewayConfigDto GetConfig();
}

using VibeTrade.Backend.Features.Payments.Dtos;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments.Gateways;

public sealed class PaymentGatewayManager(SimulatedPaymentGateway simulated) : IPaymentGatewayManager
{
    private readonly Dictionary<string, PaymentGatewayBase> _gateways =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [PaymentGatewayIds.Simulated] = simulated,
        };

    public IReadOnlyList<string> RegisteredGatewayIds { get; } =
        [PaymentGatewayIds.Simulated];

    public PaymentGatewayBase GetGateway(string? gatewayId = null)
    {
        var id = string.IsNullOrWhiteSpace(gatewayId)
            ? PaymentGatewayIds.Simulated
            : gatewayId.Trim();

        if (!_gateways.TryGetValue(id, out var gateway))
            throw new InvalidOperationException($"Pasarela de pago no registrada: {id}");

        return gateway;
    }

    public PaymentGatewayConfigDto GetConfig() =>
        new(
            Enabled: true,
            GatewayId: PaymentGatewayIds.Simulated,
            SimulatedMode: true);
}

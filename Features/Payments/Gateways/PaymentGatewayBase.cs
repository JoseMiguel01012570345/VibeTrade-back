namespace VibeTrade.Backend.Features.Payments.Gateways;

/// <summary>
/// Contrato base para pasarelas de pago. Cada implementación concreta debe proveer
/// transferencias, consulta de saldo y consulta de transacción por id.
/// </summary>
public abstract class PaymentGatewayBase
{
    public abstract string GatewayId { get; }

    public abstract Task<PaymentTransferResult> TransferAsync(
        PaymentTransferRequest request,
        CancellationToken cancellationToken = default);

    public abstract Task<PaymentBalanceResult> GetBalanceAsync(
        PaymentBalanceQuery query,
        CancellationToken cancellationToken = default);

    public abstract Task<PaymentTransactionResult?> GetTransactionAsync(
        string transactionId,
        CancellationToken cancellationToken = default);
}

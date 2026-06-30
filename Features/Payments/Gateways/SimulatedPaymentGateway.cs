using System.Collections.Concurrent;
using VibeTrade.Backend.Features.Payments.Dtos;
using VibeTrade.Backend.Features.Payments.Interfaces;

namespace VibeTrade.Backend.Features.Payments.Gateways;

/// <summary>Pasarela simulada en memoria para desarrollo, demos y pruebas.</summary>
public sealed class SimulatedPaymentGateway : PaymentGatewayBase
{
    public const string DemoPaymentMethodId = "sim_pm_demo";
    public const string PlatformAccountId = "sim_platform";

    private static readonly ConcurrentDictionary<string, long> Balances = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SimulatedTransaction> Transactions = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> IdempotencyToTransactionId = new(StringComparer.Ordinal);

    private const long DemoUserAvailableMinor = 100_000_000_00L;

    public override string GatewayId => PaymentGatewayIds.Simulated;

    public static string AccountIdForUser(string userId) => $"sim_acc_{userId.Trim()}";

    public static string GenerateSetupToken() => $"sim_seti_{Guid.NewGuid():N}";

    public IReadOnlyList<SavedCardPaymentMethodDto> ListSavedCards(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return [];

        return
        [
            new SavedCardPaymentMethodDto(
                DemoPaymentMethodId,
                "visa",
                "4242",
                12,
                2034,
                "US"),
        ];
    }

    public PaymentMethodResolve ResolvePaymentMethod(string paymentMethodId, string accountId)
    {
        var pmId = (paymentMethodId ?? "").Trim();
        var accId = (accountId ?? "").Trim();
        if (accId.Length < 4)
        {
            return new PaymentMethodResolve(
                false, null, null, null,
                "Cuenta de pago no configurada.", "payment_account_missing",
                Accepted: false);
        }

        if (TryResolveDemoPaymentMethod(pmId, accId, out var resolved))
            return resolved;

        return new PaymentMethodResolve(
            false, null, null, null,
            "La tarjeta seleccionada no pertenece a tu cuenta.", "payment_method_not_owned",
            Accepted: false);
    }

    private static bool TryResolveDemoPaymentMethod(
        string paymentMethodId,
        string accountId,
        out PaymentMethodResolve result)
    {
        if (string.Equals(paymentMethodId, DemoPaymentMethodId, StringComparison.Ordinal))
        {
            result = new PaymentMethodResolve(
                true,
                DemoPaymentMethodId,
                "visa",
                "4242",
                null,
                null,
                Accepted: false);
            return true;
        }

        // Tests y demos pueden usar IDs estilo Stripe (pm_*) contra cuentas simuladas del usuario.
        if (IsDemoUserAccount(accountId)
            && paymentMethodId.Length >= 12
            && (paymentMethodId.StartsWith("pm_", StringComparison.Ordinal)
                || paymentMethodId.StartsWith("sim_pm_", StringComparison.Ordinal)))
        {
            result = new PaymentMethodResolve(
                true,
                paymentMethodId,
                "visa",
                "4242",
                null,
                null,
                Accepted: false);
            return true;
        }

        result = default!;
        return false;
    }

    public override Task<PaymentTransferResult> TransferAsync(
        PaymentTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var source = (request.SourceAccountId ?? "").Trim();
        var dest = (request.DestinationAccountId ?? "").Trim();
        var currency = (request.Currency ?? "").Trim().ToLowerInvariant();
        var amount = request.AmountMinor;
        var idem = (request.IdempotencyKey ?? "").Trim();

        if (source.Length < 2 || dest.Length < 2 || currency.Length is < 3 or > 8 || amount <= 0)
        {
            return Task.FromResult(new PaymentTransferResult(
                false, null, "invalid_transfer", "Parámetros de transferencia inválidos."));
        }

        if (idem.Length >= 8
            && IdempotencyToTransactionId.TryGetValue(BuildIdempotencyKey(idem), out var existingId)
            && Transactions.TryGetValue(existingId, out var existing))
        {
            return Task.FromResult(new PaymentTransferResult(
                true, existing.Id, null, null));
        }

        if (!CanDebit(source, currency, amount))
        {
            return Task.FromResult(new PaymentTransferResult(
                false, null, "insufficient_funds", "Saldo insuficiente para la transferencia."));
        }

        Debit(source, currency, amount);
        Credit(dest, currency, amount);

        var txnId = $"sim_tx_{Guid.NewGuid():N}";
        var txn = new SimulatedTransaction(
            txnId,
            "succeeded",
            source,
            dest,
            currency,
            amount,
            DateTimeOffset.UtcNow,
            request.Metadata is null
                ? null
                : new Dictionary<string, string>(request.Metadata, StringComparer.Ordinal));

        Transactions[txnId] = txn;
        if (idem.Length >= 8)
            IdempotencyToTransactionId[BuildIdempotencyKey(idem)] = txnId;

        return Task.FromResult(new PaymentTransferResult(true, txnId, null, null));
    }

    public override Task<PaymentBalanceResult> GetBalanceAsync(
        PaymentBalanceQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var accountId = (query.AccountId ?? "").Trim();
        var currency = (query.Currency ?? "").Trim().ToLowerInvariant();
        if (accountId.Length < 2 || currency.Length is < 3 or > 8)
        {
            return Task.FromResult(new PaymentBalanceResult(
                false, 0, "invalid_balance_query", "Parámetros de consulta inválidos."));
        }

        var available = IsDemoUserAccount(accountId)
            ? DemoUserAvailableMinor
            : GetBalanceMinor(accountId, currency);

        return Task.FromResult(new PaymentBalanceResult(true, available, null, null));
    }

    public override Task<PaymentTransactionResult?> GetTransactionAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = (transactionId ?? "").Trim();
        if (id.Length < 8 || !Transactions.TryGetValue(id, out var txn))
            return Task.FromResult<PaymentTransactionResult?>(null);

        return Task.FromResult<PaymentTransactionResult?>(txn.ToResult());
    }

    private static bool IsDemoUserAccount(string accountId) =>
        accountId.StartsWith("sim_acc_", StringComparison.Ordinal);

    private static string BalanceKey(string accountId, string currency) =>
        $"{accountId}:{currency}";

    private static string BuildIdempotencyKey(string idempotencyKey) =>
        idempotencyKey.Trim();

    private static long GetBalanceMinor(string accountId, string currency) =>
        Balances.GetValueOrDefault(BalanceKey(accountId, currency));

    private static bool CanDebit(string accountId, string currency, long amount)
    {
        if (IsDemoUserAccount(accountId))
            return true;

        return GetBalanceMinor(accountId, currency) >= amount;
    }

    private static void Debit(string accountId, string currency, long amount)
    {
        if (IsDemoUserAccount(accountId))
            return;

        var key = BalanceKey(accountId, currency);
        Balances.AddOrUpdate(key, 0, (_, current) => current - amount);
    }

    private static void Credit(string accountId, string currency, long amount)
    {
        if (IsDemoUserAccount(accountId))
            return;

        var key = BalanceKey(accountId, currency);
        Balances.AddOrUpdate(key, amount, (_, current) => current + amount);
    }

    private sealed record SimulatedTransaction(
        string Id,
        string Status,
        string SourceAccountId,
        string DestinationAccountId,
        string Currency,
        long AmountMinor,
        DateTimeOffset CreatedAtUtc,
        IReadOnlyDictionary<string, string>? Metadata)
    {
        public PaymentTransactionResult ToResult() =>
            new(
                Id,
                Status,
                SourceAccountId,
                DestinationAccountId,
                Currency,
                AmountMinor,
                CreatedAtUtc,
                Metadata);
    }
}

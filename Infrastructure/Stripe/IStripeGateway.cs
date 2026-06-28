using VibeTrade.Backend.Features.Payments.Dtos;

namespace VibeTrade.Backend.Infrastructure.Stripe;

public sealed record StripePaymentMethodResolve(
    bool Success,
    string? PaymentMethodId,
    string? CardBrand,
    string? CardLast4,
    string? ErrorMessage,
    string? ErrorCode,
    bool Accepted);

public sealed record StripeChargeResult(
    bool Success,
    string? PaymentIntentId,
    string? ClientSecret,
    string? Status,
    string? ErrorMessage,
    string? ErrorCode,
    bool Accepted,
    long? ActualFeeMinor);

public interface IStripeGateway
{
    StripeConfigDto GetConfig();

    bool SkipPaymentIntents { get; }

    string GenerateSkipModeCustomerId();

    string GenerateSkipModeSetupIntentId();

    Task<string?> CreateCustomerAsync(
        string userId,
        string? displayName,
        string? email,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StripeCardPaymentMethodDto>> ListCardPaymentMethodsAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    Task<StripePaymentMethodResolve> ResolveCustomerPaymentMethodAsync(
        string paymentMethodId,
        string customerId,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, string? ErrorMessage, CreateSetupIntentResult? Result)> CreateSetupIntentAsync(
        string customerId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, string? ErrorCode, string? ErrorMessage, CreatePaymentIntentResult? Result)> CreateCheckoutPaymentIntentAsync(
        string buyerUserId,
        string customerId,
        string paymentMethodId,
        string threadId,
        string agreementId,
        string currencyLower,
        long amountMinor,
        CancellationToken cancellationToken = default);

    Task<StripeChargeResult> CreateAndConfirmPaymentIntentAsync(
        string customerId,
        string paymentMethodId,
        string agreementId,
        string currency,
        long amountMinor,
        CancellationToken cancellationToken = default);

    Task<long?> GetActualStripeFeeMinorAsync(
        string paymentIntentId,
        long estimatedFeeMinor,
        CancellationToken cancellationToken = default);
}

namespace Fiore.Api.Features.Payments;

public enum PaymentGatewayOutcome
{
    Succeeded,
    Declined,
    ProviderUnavailable
}

public sealed record PaymentGatewayRequest(
    Guid OrderId,
    string ProviderPaymentId,
    decimal Amount,
    string Currency,
    string? SimulatedOutcome = null);

public sealed record PaymentGatewayResult(
    PaymentGatewayOutcome Outcome,
    string? ProviderReference = null,
    string? ErrorMessage = null)
{
    public static PaymentGatewayResult Succeeded(string providerReference) =>
        new(PaymentGatewayOutcome.Succeeded, providerReference);

    public static PaymentGatewayResult Declined(string errorMessage) =>
        new(PaymentGatewayOutcome.Declined, ErrorMessage: errorMessage);

    public static PaymentGatewayResult ProviderUnavailable(string errorMessage) =>
        new(PaymentGatewayOutcome.ProviderUnavailable, ErrorMessage: errorMessage);
}

/// <summary>
/// Abstraction over an external payment provider (NFR-05.1).
/// Swap <see cref="SandboxPaymentGateway"/> with a production provider implementation in DI.
/// </summary>
public interface IPaymentGateway
{
    string ProviderName { get; }

    Task<PaymentGatewayResult> ChargeAsync(PaymentGatewayRequest request, CancellationToken cancellationToken = default);
}

public sealed class SandboxPaymentGateway : IPaymentGateway
{
    public string ProviderName => "sandbox";

    public Task<PaymentGatewayResult> ChargeAsync(PaymentGatewayRequest request, CancellationToken cancellationToken = default)
    {
        var outcome = request.SimulatedOutcome?.Trim();
        if (string.Equals(outcome, "declined", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PaymentGatewayResult.Declined(
                "Оплату відхилено платіжним провайдером. Спробуйте ще раз або використайте іншу картку."));
        }

        if (string.Equals(outcome, "providerUnavailable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outcome, "provider_error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outcome, "timeout", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PaymentGatewayResult.ProviderUnavailable(
                "Платіжний сервіс тимчасово недоступний. Замовлення збережено, спробуйте повторити оплату."));
        }

        return Task.FromResult(PaymentGatewayResult.Succeeded(request.ProviderPaymentId));
    }
}

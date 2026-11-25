using Fiore.Api.Features.Carts;
using Fiore.Api.Features.Orders;
using Fiore.Api.Domain.Commerce;
using Microsoft.AspNetCore.OutputCaching;

namespace Fiore.Api.Features.Payments;

public sealed record PaymentResponse(Guid PaymentId, string Provider, string ProviderPaymentId, PaymentStatus PaymentStatus, decimal Amount, string Currency, OrderStatus OrderStatus);

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/orders/{orderId:guid}/payments/sandbox/confirm", async (Guid orderId, HttpRequest httpRequest, OrderLifecycleService lifecycleService, IOutputCacheStore outputCacheStore, CancellationToken cancellationToken) =>
        {
            if (!CartEndpointResults.TryGetToken(httpRequest, out var token)) return CartEndpointResults.MissingToken();
            var result = await lifecycleService.ConfirmSandboxPayment(orderId, token, cancellationToken);
            if (result.Error == PaymentError.PaymentWindowExpired)
            {
                // The late attempt may have just expired the order and returned its stock.
                await outputCacheStore.EvictByTagAsync("catalog", cancellationToken);
            }
            return result.Error switch
            {
                null => Results.Ok(ToResponse(result.Payment!, result.OrderStatus!.Value)),
                PaymentError.NotFound => Results.NotFound(),
                _ => Results.Conflict(new { error = result.Error.ToString() })
            };
        }).WithName("ConfirmSandboxPayment").WithOpenApi();

        return endpoints;
    }

    private static PaymentResponse ToResponse(Domain.Commerce.Payment payment, Domain.Commerce.OrderStatus orderStatus) => new(
        payment.Id,
        payment.Provider,
        payment.ProviderPaymentId,
        payment.Status,
        payment.Amount,
        payment.Currency,
        orderStatus);
}

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
        endpoints.MapPost("/api/v1/orders/{orderId:guid}/payments/confirm", ConfirmPaymentAsync)
            .WithName("ConfirmPayment")
            .WithOpenApi();

        endpoints.MapPost("/api/v1/orders/{orderId:guid}/payments/sandbox/confirm", ConfirmPaymentAsync)
            .WithName("ConfirmSandboxPayment")
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<IResult> ConfirmPaymentAsync(
        Guid orderId,
        string? outcome,
        HttpRequest httpRequest,
        IHostEnvironment environment,
        OrderLifecycleService lifecycleService,
        IOutputCacheStore outputCacheStore,
        CancellationToken cancellationToken)
    {
        if (!CartEndpointResults.TryGetToken(httpRequest, out var token)) return CartEndpointResults.MissingToken();

        var allowSimulation = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var simulatedOutcome = allowSimulation
            ? outcome ?? httpRequest.Headers["X-Sandbox-Outcome"].FirstOrDefault()
            : null;

        var result = await lifecycleService.ConfirmSandboxPayment(orderId, token, cancellationToken, simulatedOutcome);
        if (result.Error == PaymentError.PaymentWindowExpired)
        {
            // The late attempt may have just expired the order and returned its stock.
            await outputCacheStore.EvictByTagAsync("catalog", cancellationToken);
        }

        return result.Error switch
        {
            null => Results.Ok(ToResponse(result.Payment!, result.OrderStatus!.Value)),
            PaymentError.NotFound => Results.NotFound(),
            PaymentError.ProviderUnavailable => Results.Json(new
            {
                error = "PaymentProviderUnavailable",
                message = result.ErrorMessage ?? "Платіжний сервіс тимчасово недоступний. Замовлення збережено, спробуйте повторити оплату.",
                retryable = true
            }, statusCode: StatusCodes.Status502BadGateway),
            PaymentError.PaymentDeclined => Results.Conflict(new
            {
                error = "PaymentDeclined",
                message = result.ErrorMessage ?? "Оплату відхилено платіжним провайдером. Спробуйте іншу картку.",
                retryable = true
            }),
            PaymentError.PaymentWindowExpired => Results.Conflict(new
            {
                error = "PaymentWindowExpired",
                message = "Час на оплату замовлення вичерпано. Замовлення скасовано, а букети повернуто в наявність.",
                retryable = false
            }),
            _ => Results.Conflict(new
            {
                error = result.Error.ToString(),
                message = "Це замовлення не можна оплатити в поточному статусі.",
                retryable = false
            })
        };
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

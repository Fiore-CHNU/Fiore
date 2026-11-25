using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Features.Carts;
using Fiore.Api.Features.Delivery;
using Fiore.Api.Infrastructure.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Checkout;

public sealed record CheckoutRequest(
    Guid CartId,
    string ContactName,
    string ContactEmail,
    string ContactPhone,
    string RecipientName,
    string RecipientPhone,
    string City,
    string Street,
    string Building,
    DateTimeOffset ScheduledFrom,
    DateTimeOffset ScheduledTo,
    string? Apartment = null,
    string? DeliveryNotes = null,
    string? CustomerNote = null,
    string? CardMessage = null);

public sealed record CheckoutCommand(CheckoutRequest Request, Guid SessionToken) : IRequest<CheckoutResult>;
public sealed record GetOrderRequest(Guid OrderId, Guid SessionToken) : IRequest<OrderDetailsResponse?>;
public sealed record CheckoutResponse(Guid OrderId, OrderStatus Status, decimal SubtotalAmount, decimal DeliveryFeeAmount, decimal TotalAmount, string Currency, DateTimeOffset CreatedAt);
public sealed record OrderDetailsResponse(
    Guid OrderId,
    OrderStatus Status,
    decimal SubtotalAmount,
    decimal DeliveryFeeAmount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OrderItemResponse> Items,
    DeliveryDetailsResponse Delivery,
    PaymentDetailsResponse? Payment,
    IReadOnlyList<OrderStatusHistoryResponse> StatusHistory);
public sealed record OrderItemResponse(Guid ProductId, string Name, string Sku, decimal UnitPrice, int Quantity, decimal LineTotal);
public sealed record DeliveryDetailsResponse(string RecipientName, string RecipientPhone, string City, string Street, string Building, string? Apartment, string? DeliveryNotes, DateTimeOffset ScheduledFrom, DateTimeOffset ScheduledTo);
public sealed record PaymentDetailsResponse(string Provider, PaymentStatus Status, decimal Amount, string Currency);
public sealed record OrderStatusHistoryResponse(OrderStatus Status, string? Note, DateTimeOffset ChangedAt);

public enum CheckoutError
{
    CartNotFound,
    InvalidSession,
    CartInactive,
    CartEmpty,
    ProductNotAvailable,
    QuantityExceedsAvailability,
    MixedCurrencies,
    DeliverySlotUnavailable,
    DeliverySlotInvalid
}

public sealed record CheckoutResult(CheckoutResponse? Order, CheckoutError? Error)
{
    public static CheckoutResult Success(Order order) => new(new CheckoutResponse(
        order.Id,
        order.Status,
        order.SubtotalAmount,
        order.DeliveryFeeAmount,
        order.TotalAmount,
        order.Currency,
        order.CreatedAt), null);

    public static CheckoutResult Failure(CheckoutError error) => new(null, error);
}

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(request => request.CartId).NotEmpty();
        RuleFor(request => request.ContactName).NotEmpty().MaximumLength(120);
        RuleFor(request => request.ContactEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.ContactPhone).NotEmpty().MaximumLength(32);
        RuleFor(request => request.RecipientName).NotEmpty().MaximumLength(120);
        RuleFor(request => request.RecipientPhone).NotEmpty().MaximumLength(32);
        RuleFor(request => request.City).NotEmpty().MaximumLength(120);
        RuleFor(request => request.Street).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Building).NotEmpty().MaximumLength(32);
        RuleFor(request => request.ScheduledTo).GreaterThan(request => request.ScheduledFrom);
        RuleFor(request => request.Apartment).MaximumLength(32).When(request => request.Apartment is not null);
        RuleFor(request => request.DeliveryNotes).MaximumLength(1000).When(request => request.DeliveryNotes is not null);
        RuleFor(request => request.CustomerNote).MaximumLength(1000).When(request => request.CustomerNote is not null);
        RuleFor(request => request.CardMessage).MaximumLength(500).When(request => request.CardMessage is not null);
    }
}

public sealed class CheckoutHandler(FioreDbContext dbContext, DeliverySlotService deliverySlotService) : IRequestHandler<CheckoutCommand, CheckoutResult>
{
    public async Task<CheckoutResult> Handle(CheckoutCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var cart = await dbContext.Carts
            .Include(candidate => candidate.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(product => product.Inventory)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.CartId, cancellationToken);

        if (cart is null) return CheckoutResult.Failure(CheckoutError.CartNotFound);
        if (cart.SessionToken != command.SessionToken) return CheckoutResult.Failure(CheckoutError.InvalidSession);
        if (cart.Status != CartStatus.Active) return CheckoutResult.Failure(CheckoutError.CartInactive);
        if (cart.Items.Count == 0) return CheckoutResult.Failure(CheckoutError.CartEmpty);
        if (!deliverySlotService.IsConfiguredSlot(request.ScheduledFrom, request.ScheduledTo)) return CheckoutResult.Failure(CheckoutError.DeliverySlotInvalid);
        if (!await deliverySlotService.HasCapacity(null, request.ScheduledFrom, request.ScheduledTo, cancellationToken)) return CheckoutResult.Failure(CheckoutError.DeliverySlotUnavailable);

        foreach (var item in cart.Items)
        {
            if (item.Product.Status != ProductStatus.Available || item.Product.Inventory is null || item.Product.Inventory.QuantityAvailable <= 0)
            {
                return CheckoutResult.Failure(CheckoutError.ProductNotAvailable);
            }

            if (item.Quantity > item.Product.Inventory.QuantityAvailable)
            {
                return CheckoutResult.Failure(CheckoutError.QuantityExceedsAvailability);
            }
        }

        var currency = cart.Items[0].Product.Currency;
        if (cart.Items.Any(item => item.Product.Currency != currency))
        {
            return CheckoutResult.Failure(CheckoutError.MixedCurrencies);
        }

        var now = DateTimeOffset.UtcNow;
        var subtotal = cart.Items.Sum(item => item.Product.BasePrice * item.Quantity);
        var deliveryFee = deliverySlotService.CalculateFee(subtotal);
        var orderId = Guid.NewGuid();
        var order = new Order
        {
            Id = orderId,
            SourceCartId = cart.Id,
            ContactName = request.ContactName.Trim(),
            ContactEmail = request.ContactEmail.Trim(),
            ContactPhone = request.ContactPhone.Trim(),
            Status = OrderStatus.PendingPayment,
            Currency = currency,
            SubtotalAmount = subtotal,
            DeliveryFeeAmount = deliveryFee,
            TotalAmount = subtotal + deliveryFee,
            CustomerNote = request.CustomerNote?.Trim(),
            CardMessage = request.CardMessage?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Delivery = new OrderDelivery
            {
                OrderId = orderId,
                RecipientName = request.RecipientName.Trim(),
                RecipientPhone = request.RecipientPhone.Trim(),
                City = request.City.Trim(),
                Street = request.Street.Trim(),
                Building = request.Building.Trim(),
                Apartment = request.Apartment?.Trim(),
                DeliveryNotes = request.DeliveryNotes?.Trim(),
                ScheduledFrom = request.ScheduledFrom,
                ScheduledTo = request.ScheduledTo
            },
            Items = cart.Items.Select(item => new OrderItem
            {
                Id = Guid.NewGuid(),
                ProductId = item.ProductId,
                ProductName = item.Product.Name,
                ProductSku = item.Product.Sku,
                UnitPrice = item.Product.BasePrice,
                Quantity = item.Quantity,
                LineTotal = item.Product.BasePrice * item.Quantity
            }).ToList(),
            Payment = new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Provider = "sandbox",
                ProviderPaymentId = $"sandbox-{Guid.NewGuid():N}",
                Status = PaymentStatus.Initiated,
                Amount = subtotal + deliveryFee,
                Currency = currency,
                CreatedAt = now,
                UpdatedAt = now
            },
            StatusHistory =
            [
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Status = OrderStatus.PendingPayment,
                    Note = "Order created; awaiting payment.",
                    ChangedAt = now
                }
            ]
        };

        foreach (var item in cart.Items)
        {
            item.Product.Inventory!.QuantityAvailable -= item.Quantity;
            item.Product.Inventory.UpdatedAt = now;
        }

        cart.Status = CartStatus.Converted;
        cart.UpdatedAt = now;
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CheckoutResult.Success(order);
    }
}

public sealed class GetOrderHandler(FioreDbContext dbContext) : IRequestHandler<GetOrderRequest, OrderDetailsResponse?>
{
    public async Task<OrderDetailsResponse?> Handle(GetOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.Delivery)
            .Include(candidate => candidate.Payment)
            .Include(candidate => candidate.StatusHistory)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.OrderId &&
                dbContext.Carts.Any(cart => cart.Id == candidate.SourceCartId && cart.SessionToken == request.SessionToken),
                cancellationToken);

        if (order is null || order.Delivery is null)
        {
            return null;
        }

        return new OrderDetailsResponse(
            order.Id,
            order.Status,
            order.SubtotalAmount,
            order.DeliveryFeeAmount,
            order.TotalAmount,
            order.Currency,
            order.CreatedAt,
            order.Items.Select(item => new OrderItemResponse(item.ProductId, item.ProductName, item.ProductSku, item.UnitPrice, item.Quantity, item.LineTotal)).ToList(),
            new DeliveryDetailsResponse(
                order.Delivery.RecipientName,
                order.Delivery.RecipientPhone,
                order.Delivery.City,
                order.Delivery.Street,
                order.Delivery.Building,
                order.Delivery.Apartment,
                order.Delivery.DeliveryNotes,
                order.Delivery.ScheduledFrom,
                order.Delivery.ScheduledTo),
            order.Payment is null ? null : new PaymentDetailsResponse(order.Payment.Provider, order.Payment.Status, order.Payment.Amount, order.Payment.Currency),
            order.StatusHistory.OrderBy(history => history.ChangedAt).Select(history => new OrderStatusHistoryResponse(history.Status, history.Note, history.ChangedAt)).ToList());
    }
}

public static class CheckoutEndpoints
{
    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/orders/{orderId:guid}", async (Guid orderId, HttpRequest httpRequest, ISender sender, CancellationToken cancellationToken) =>
        {
            if (!CartEndpointResults.TryGetToken(httpRequest, out var token)) return CartEndpointResults.MissingToken();
            var order = await sender.Send(new GetOrderRequest(orderId, token), cancellationToken);
            return order is null ? Results.NotFound() : Results.Ok(order);
        }).WithName("GetGuestOrder").WithOpenApi();

        endpoints.MapPost("/api/v1/checkout", async (
            CheckoutRequest request,
            HttpRequest httpRequest,
            IValidator<CheckoutRequest> validator,
            ISender sender,
            IOutputCacheStore outputCacheStore,
            CancellationToken cancellationToken) =>
        {
            if (!CartEndpointResults.TryGetToken(httpRequest, out var token)) return CartEndpointResults.MissingToken();
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await sender.Send(new CheckoutCommand(request, token), cancellationToken);
            if (result.Error is null)
            {
                await outputCacheStore.EvictByTagAsync("catalog", cancellationToken);
                return Results.Created($"/api/v1/orders/{result.Order!.OrderId}", result.Order);
            }

            return result.Error switch
            {
                CheckoutError.CartNotFound => Results.NotFound(),
                CheckoutError.InvalidSession => Results.Forbid(),
                CheckoutError.QuantityExceedsAvailability or CheckoutError.DeliverySlotUnavailable => Results.Conflict(new { error = result.Error.ToString() }),
                _ => Results.BadRequest(new { error = result.Error!.ToString() })
            };
        }).WithName("CheckoutGuestCart").WithOpenApi();

        return endpoints;
    }
}

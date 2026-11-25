using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Features.Common;
using Fiore.Api.Features.Delivery;
using Fiore.Api.Infrastructure.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Carts;

public sealed record CartResponse(
    Guid Id,
    Guid SessionToken,
    CartStatus Status,
    IReadOnlyList<CartItemResponse> Items,
    decimal SubtotalAmount,
    decimal DeliveryFeeAmount,
    decimal TotalAmount,
    decimal MinimumOrderAmount,
    bool MeetsMinimumOrder,
    bool HasPriceChanges,
    bool HasUnavailableItems,
    string Currency);

/// <summary>
/// <paramref name="UnitPrice"/> is the current catalogue price; <paramref name="AcceptedUnitPrice"/> is the price the customer saw
/// when adding the item. When they differ, checkout returns 409 until the customer accepts the new prices (US-4.5).
/// </summary>
public sealed record CartItemResponse(Guid ProductId, string Sku, string Name, decimal UnitPrice, decimal AcceptedUnitPrice, bool PriceChanged, string Currency, string? ImageUrl, bool IsAvailable, int Quantity, decimal LineTotal);
public sealed record AddCartItemRequest(Guid ProductId, int Quantity);
public sealed record UpdateCartItemRequest(int Quantity);
public sealed record AddCartItemCommand(Guid CartId, Guid SessionToken, Guid ProductId, int Quantity) : IRequest<CartOperationResult>;
public sealed record UpdateCartItemCommand(Guid CartId, Guid SessionToken, Guid ProductId, int Quantity) : IRequest<CartOperationResult>;
public sealed record GetCartRequest(Guid CartId, Guid SessionToken) : IRequest<CartOperationResult>;
public sealed record RemoveCartItemRequest(Guid CartId, Guid ProductId, Guid SessionToken) : IRequest<CartOperationResult>;
public sealed record CreateCartRequest : IRequest<CartResponse>;
public sealed record AcceptCartPricesCommand(Guid CartId, Guid SessionToken) : IRequest<CartOperationResult>;

public enum CartError
{
    NotFound,
    InvalidSession,
    Inactive,
    ItemNotFound,
    ProductNotAvailable,
    QuantityExceedsAvailability
}

public sealed record CartOperationResult(CartResponse? Cart, CartError? Error)
{
    public static CartOperationResult Success(Cart cart, DeliveryOptions options) => new(CartMapper.ToResponse(cart, options), null);
    public static CartOperationResult Failure(CartError error) => new(null, error);
}

public sealed class CreateCartHandler(FioreDbContext dbContext, DeliveryOptions options) : IRequestHandler<CreateCartRequest, CartResponse>
{
    public async Task<CartResponse> Handle(CreateCartRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            SessionToken = Guid.NewGuid(),
            Status = CartStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Carts.Add(cart);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartMapper.ToResponse(cart, options);
    }
}

public sealed class GetCartHandler(FioreDbContext dbContext, DeliveryOptions options) : IRequestHandler<GetCartRequest, CartOperationResult>
{
    public async Task<CartOperationResult> Handle(GetCartRequest request, CancellationToken cancellationToken)
    {
        var cart = await CartMapper.LoadCart(dbContext, request.CartId, cancellationToken);
        return CartMapper.ValidateSession(cart, request.SessionToken) is { } error
            ? CartOperationResult.Failure(error)
            : CartOperationResult.Success(cart!, options);
    }
}

public sealed class AddCartItemHandler(FioreDbContext dbContext, DeliveryOptions options) : IRequestHandler<AddCartItemCommand, CartOperationResult>
{
    public async Task<CartOperationResult> Handle(AddCartItemCommand request, CancellationToken cancellationToken)
    {
        var cart = await CartMapper.LoadCart(dbContext, request.CartId, cancellationToken);
        if (CartMapper.ValidateSession(cart, request.SessionToken) is { } error)
        {
            return CartOperationResult.Failure(error);
        }

        var product = await dbContext.Products
            .Include(item => item.Inventory)
            .FirstOrDefaultAsync(item => item.Id == request.ProductId, cancellationToken);

        if (product is null || !CommerceRules.IsOrderable(product))
        {
            return CartOperationResult.Failure(CartError.ProductNotAvailable);
        }

        var existingItem = cart!.Items.SingleOrDefault(item => item.ProductId == request.ProductId);
        var requestedQuantity = (existingItem?.Quantity ?? 0) + request.Quantity;
        if (requestedQuantity > product.Inventory!.QuantityAvailable)
        {
            return CartOperationResult.Failure(CartError.QuantityExceedsAvailability);
        }

        if (existingItem is null)
        {
            cart.Items.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = product.Id,
                Product = product,
                Quantity = request.Quantity,
                UnitPrice = product.BasePrice,
                AddedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existingItem.Quantity = requestedQuantity;
        }

        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartOperationResult.Success(cart, options);
    }
}

public sealed class UpdateCartItemHandler(FioreDbContext dbContext, DeliveryOptions options) : IRequestHandler<UpdateCartItemCommand, CartOperationResult>
{
    public async Task<CartOperationResult> Handle(UpdateCartItemCommand request, CancellationToken cancellationToken)
    {
        var cart = await CartMapper.LoadCart(dbContext, request.CartId, cancellationToken);
        if (CartMapper.ValidateSession(cart, request.SessionToken) is { } error)
        {
            return CartOperationResult.Failure(error);
        }

        var item = cart!.Items.SingleOrDefault(candidate => candidate.ProductId == request.ProductId);
        if (item is null)
        {
            return CartOperationResult.Failure(CartError.ItemNotFound);
        }

        if (!CommerceRules.IsOrderable(item.Product))
        {
            return CartOperationResult.Failure(CartError.ProductNotAvailable);
        }

        if (request.Quantity > item.Product.Inventory!.QuantityAvailable)
        {
            return CartOperationResult.Failure(CartError.QuantityExceedsAvailability);
        }

        item.Quantity = request.Quantity;
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartOperationResult.Success(cart, options);
    }
}

public sealed class RemoveCartItemHandler(FioreDbContext dbContext, DeliveryOptions options) : IRequestHandler<RemoveCartItemRequest, CartOperationResult>
{
    public async Task<CartOperationResult> Handle(RemoveCartItemRequest request, CancellationToken cancellationToken)
    {
        var cart = await CartMapper.LoadCart(dbContext, request.CartId, cancellationToken);
        if (CartMapper.ValidateSession(cart, request.SessionToken) is { } error)
        {
            return CartOperationResult.Failure(error);
        }

        var item = cart!.Items.SingleOrDefault(candidate => candidate.ProductId == request.ProductId);
        if (item is null)
        {
            return CartOperationResult.Failure(CartError.ItemNotFound);
        }

        dbContext.CartItems.Remove(item);
        cart.Items.Remove(item);
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartOperationResult.Success(cart, options);
    }
}

public sealed class AcceptCartPricesHandler(FioreDbContext dbContext, DeliveryOptions options) : IRequestHandler<AcceptCartPricesCommand, CartOperationResult>
{
    public async Task<CartOperationResult> Handle(AcceptCartPricesCommand request, CancellationToken cancellationToken)
    {
        var cart = await CartMapper.LoadCart(dbContext, request.CartId, cancellationToken);
        if (CartMapper.ValidateSession(cart, request.SessionToken) is { } error)
        {
            return CartOperationResult.Failure(error);
        }

        foreach (var item in cart!.Items)
        {
            item.UnitPrice = item.Product.BasePrice;
        }

        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartOperationResult.Success(cart, options);
    }
}

public sealed class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Quantity).InclusiveBetween(1, 99);
    }
}

public sealed class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
{
    public UpdateCartItemRequestValidator() => RuleFor(request => request.Quantity).InclusiveBetween(1, 99);
}

internal static class CartMapper
{
    public static Task<Cart?> LoadCart(FioreDbContext dbContext, Guid cartId, CancellationToken cancellationToken) => dbContext.Carts
        .Include(cart => cart.Items)
        .ThenInclude(item => item.Product)
        .ThenInclude(product => product.Images)
        .Include(cart => cart.Items)
        .ThenInclude(item => item.Product)
        .ThenInclude(product => product.Inventory)
        .SingleOrDefaultAsync(cart => cart.Id == cartId, cancellationToken);

    public static CartError? ValidateSession(Cart? cart, Guid token)
    {
        if (cart is null) return CartError.NotFound;
        if (cart.SessionToken != token) return CartError.InvalidSession;
        return cart.Status == CartStatus.Active ? null : CartError.Inactive;
    }

    public static CartResponse ToResponse(Cart cart, DeliveryOptions options)
    {
        var items = cart.Items
            .OrderBy(item => item.AddedAt)
            .Select(item => new CartItemResponse(
                item.ProductId,
                item.Product.Sku,
                item.Product.Name,
                item.Product.BasePrice,
                item.UnitPrice,
                item.UnitPrice != item.Product.BasePrice,
                item.Product.Currency,
                item.Product.Images.OrderBy(image => image.SortOrder).Select(image => image.ImageUrl).FirstOrDefault(),
                CommerceRules.IsOrderable(item.Product),
                item.Quantity,
                item.Product.BasePrice * item.Quantity))
            .ToList();

        // Totals count only items that can still be ordered, so the customer sees what checkout would charge.
        var subtotal = items.Where(item => item.IsAvailable).Sum(item => item.LineTotal);
        var deliveryFee = subtotal == 0 ? 0 : subtotal >= options.FreeDeliveryFromUah ? 0 : options.FlatFeeUah;
        return new CartResponse(
            cart.Id,
            cart.SessionToken,
            cart.Status,
            items,
            subtotal,
            deliveryFee,
            subtotal + deliveryFee,
            options.MinimumOrderUah,
            subtotal >= options.MinimumOrderUah,
            items.Any(item => item.PriceChanged),
            items.Any(item => !item.IsAvailable),
            items.FirstOrDefault()?.Currency ?? "UAH");
    }
}

public static class CartEndpointResults
{
    public static IResult ToHttpResult(CartOperationResult result) => result.Error switch
    {
        null => Results.Ok(result.Cart),
        CartError.NotFound or CartError.ItemNotFound => Results.NotFound(),
        CartError.InvalidSession => Results.Forbid(),
        CartError.Inactive => Results.BadRequest(new { error = result.Error.ToString() }),
        CartError.ProductNotAvailable or CartError.QuantityExceedsAvailability => Results.Conflict(new { error = result.Error.ToString() }),
        _ => Results.BadRequest()
    };

    public static bool TryGetToken(HttpRequest request, out Guid token) => Guid.TryParse(request.Headers["X-Cart-Token"], out token);

    public static IResult MissingToken() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["X-Cart-Token"] = ["A valid X-Cart-Token header is required."]
    });
}

public static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/carts", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var cart = await sender.Send(new CreateCartRequest(), cancellationToken);
            return Results.Created($"/api/v1/carts/{cart.Id}", cart);
        }).WithName("CreateCart").WithOpenApi();

        endpoints.MapGet("/api/v1/carts/{cartId:guid}", async (Guid cartId, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            if (!CartEndpointResults.TryGetToken(request, out var token)) return CartEndpointResults.MissingToken();
            return CartEndpointResults.ToHttpResult(await sender.Send(new GetCartRequest(cartId, token), cancellationToken));
        }).WithName("GetCart").WithOpenApi();

        endpoints.MapPost("/api/v1/carts/{cartId:guid}/items", async (Guid cartId, AddCartItemRequest body, HttpRequest request, IValidator<AddCartItemRequest> validator, ISender sender, CancellationToken cancellationToken) =>
        {
            if (!CartEndpointResults.TryGetToken(request, out var token)) return CartEndpointResults.MissingToken();
            var validation = await validator.ValidateAsync(body, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            return CartEndpointResults.ToHttpResult(await sender.Send(new AddCartItemCommand(cartId, token, body.ProductId, body.Quantity), cancellationToken));
        }).WithName("AddCartItem").WithOpenApi();

        endpoints.MapPut("/api/v1/carts/{cartId:guid}/items/{productId:guid}", async (Guid cartId, Guid productId, UpdateCartItemRequest body, HttpRequest request, IValidator<UpdateCartItemRequest> validator, ISender sender, CancellationToken cancellationToken) =>
        {
            if (!CartEndpointResults.TryGetToken(request, out var token)) return CartEndpointResults.MissingToken();
            var validation = await validator.ValidateAsync(body, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            return CartEndpointResults.ToHttpResult(await sender.Send(new UpdateCartItemCommand(cartId, token, productId, body.Quantity), cancellationToken));
        }).WithName("UpdateCartItem").WithOpenApi();

        endpoints.MapPost("/api/v1/carts/{cartId:guid}/accept-prices", async (Guid cartId, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            if (!CartEndpointResults.TryGetToken(request, out var token)) return CartEndpointResults.MissingToken();
            return CartEndpointResults.ToHttpResult(await sender.Send(new AcceptCartPricesCommand(cartId, token), cancellationToken));
        }).WithName("AcceptCartPrices").WithOpenApi();

        endpoints.MapDelete("/api/v1/carts/{cartId:guid}/items/{productId:guid}", async (Guid cartId, Guid productId, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            if (!CartEndpointResults.TryGetToken(request, out var token)) return CartEndpointResults.MissingToken();
            return CartEndpointResults.ToHttpResult(await sender.Send(new RemoveCartItemRequest(cartId, productId, token), cancellationToken));
        }).WithName("RemoveCartItem").WithOpenApi();

        return endpoints;
    }
}

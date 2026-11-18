using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Admin;

public sealed record AdminProductImageRequest(string ImageUrl, string? AltText = null);
public sealed record CreateProductRequest(
    string Sku,
    string Name,
    decimal BasePrice,
    int QuantityAvailable,
    string? Description = null,
    int ReorderLevel = 0,
    string Currency = "UAH",
    ProductStatus Status = ProductStatus.Available,
    IReadOnlyList<AdminProductImageRequest>? Images = null);
public sealed record UpdateProductRequest(
    string Name,
    decimal BasePrice,
    int QuantityAvailable,
    string? Description = null,
    int ReorderLevel = 0,
    string Currency = "UAH",
    ProductStatus Status = ProductStatus.Available,
    IReadOnlyList<AdminProductImageRequest>? Images = null);

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(request => request.Sku).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9-]+$");
        AddCommonRules();
    }

    private void AddCommonRules()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(180);
        RuleFor(request => request.BasePrice).GreaterThanOrEqualTo(0).LessThan(1_000_000);
        RuleFor(request => request.QuantityAvailable).GreaterThanOrEqualTo(0);
        RuleFor(request => request.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Currency).NotEmpty().Length(3).Matches("^[A-Z]{3}$");
        RuleFor(request => request.Description).MaximumLength(4000).When(request => request.Description is not null);
        RuleForEach(request => request.Images).ChildRules(image =>
        {
            image.RuleFor(value => value.ImageUrl).NotEmpty().MaximumLength(2048).Must(value => Uri.IsWellFormedUriString(value, UriKind.Absolute))
                .WithMessage("Image URL must be an absolute URI.");
            image.RuleFor(value => value.AltText).MaximumLength(250).When(value => value.AltText is not null);
        });
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(180);
        RuleFor(request => request.BasePrice).GreaterThanOrEqualTo(0).LessThan(1_000_000);
        RuleFor(request => request.QuantityAvailable).GreaterThanOrEqualTo(0);
        RuleFor(request => request.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Currency).NotEmpty().Length(3).Matches("^[A-Z]{3}$");
        RuleFor(request => request.Description).MaximumLength(4000).When(request => request.Description is not null);
        RuleForEach(request => request.Images).ChildRules(image =>
        {
            image.RuleFor(value => value.ImageUrl).NotEmpty().MaximumLength(2048).Must(value => Uri.IsWellFormedUriString(value, UriKind.Absolute));
            image.RuleFor(value => value.AltText).MaximumLength(250).When(value => value.AltText is not null);
        });
    }
}

public sealed record UpdateOrderStatusRequest(OrderStatus Status);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin").RequireAuthorization("Staff");

        group.MapPost("/products", async (CreateProductRequest request, IValidator<CreateProductRequest> validator, FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var sku = request.Sku.Trim().ToUpperInvariant();
            if (await dbContext.Products.AnyAsync(product => product.Sku == sku, cancellationToken))
            {
                return Results.Conflict(new { error = "SkuAlreadyExists" });
            }

            var now = DateTimeOffset.UtcNow;
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = sku,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                BasePrice = request.BasePrice,
                Currency = request.Currency.Trim().ToUpperInvariant(),
                Status = request.Status,
                CreatedAt = now,
                UpdatedAt = now,
                Inventory = new ProductInventory
                {
                    QuantityAvailable = request.QuantityAvailable,
                    ReorderLevel = request.ReorderLevel,
                    UpdatedAt = now
                },
                Images = MapImages(request.Images, now)
            };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/v1/catalog/products/{product.Id}", ToResponse(product));
        }).WithName("CreateProduct").WithOpenApi();

        group.MapPut("/products/{productId:guid}", async (Guid productId, UpdateProductRequest request, IValidator<UpdateProductRequest> validator, FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var product = await dbContext.Products.Include(candidate => candidate.Inventory).Include(candidate => candidate.Images)
                .SingleOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);
            if (product is null) return Results.NotFound();

            var now = DateTimeOffset.UtcNow;
            product.Name = request.Name.Trim();
            product.Description = request.Description?.Trim();
            product.BasePrice = request.BasePrice;
            product.Currency = request.Currency.Trim().ToUpperInvariant();
            product.Status = request.Status;
            product.UpdatedAt = now;
            product.Inventory ??= new ProductInventory { ProductId = product.Id };
            product.Inventory.QuantityAvailable = request.QuantityAvailable;
            product.Inventory.ReorderLevel = request.ReorderLevel;
            product.Inventory.UpdatedAt = now;
            dbContext.ProductImages.RemoveRange(product.Images);
            product.Images = MapImages(request.Images, now);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToResponse(product));
        }).WithName("UpdateProduct").WithOpenApi();

        group.MapGet("/orders", async (FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var orders = await dbContext.Orders.AsNoTracking().Include(order => order.Delivery)
                .OrderByDescending(order => order.CreatedAt)
                .Select(order => new
                {
                    order.Id,
                    order.Status,
                    order.ContactName,
                    order.ContactPhone,
                    order.TotalAmount,
                    order.Currency,
                    order.CreatedAt,
                    delivery = new { order.Delivery.City, order.Delivery.ScheduledFrom, order.Delivery.ScheduledTo }
                }).ToListAsync(cancellationToken);
            return Results.Ok(orders);
        }).WithName("ListAdminOrders").WithOpenApi();

        group.MapPut("/orders/{orderId:guid}/status", async (Guid orderId, UpdateOrderStatusRequest request, FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var order = await dbContext.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
            if (order is null) return Results.NotFound();
            if (!IsValidTransition(order.Status, request.Status))
            {
                return Results.Conflict(new { error = "InvalidOrderStatusTransition", currentStatus = order.Status, requestedStatus = request.Status });
            }

            order.Status = request.Status;
            order.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { order.Id, order.Status, order.UpdatedAt });
        }).WithName("UpdateOrderStatus").WithOpenApi();

        return endpoints;
    }

    private static List<ProductImage> MapImages(IReadOnlyList<AdminProductImageRequest>? images, DateTimeOffset now) =>
        (images ?? []).Select((image, index) => new ProductImage
        {
            Id = Guid.NewGuid(),
            ImageUrl = image.ImageUrl.Trim(),
            AltText = image.AltText?.Trim(),
            SortOrder = checked((short)index),
            CreatedAt = now
        }).ToList();

    private static object ToResponse(Product product) => new
    {
        product.Id,
        product.Sku,
        product.Name,
        product.Description,
        product.BasePrice,
        product.Currency,
        product.Status,
        quantityAvailable = product.Inventory?.QuantityAvailable ?? 0,
        reorderLevel = product.Inventory?.ReorderLevel ?? 0,
        images = product.Images.OrderBy(image => image.SortOrder).Select(image => new { image.Id, image.ImageUrl, image.AltText, image.SortOrder })
    };

    private static bool IsValidTransition(OrderStatus current, OrderStatus next) => (current, next) switch
    {
        (OrderStatus.PendingPayment, OrderStatus.Paid or OrderStatus.Cancelled) => true,
        (OrderStatus.Paid, OrderStatus.Accepted or OrderStatus.Cancelled) => true,
        (OrderStatus.Accepted, OrderStatus.Assembling or OrderStatus.Cancelled) => true,
        (OrderStatus.Assembling, OrderStatus.ReadyForDelivery or OrderStatus.Cancelled) => true,
        (OrderStatus.ReadyForDelivery, OrderStatus.OutForDelivery or OrderStatus.Cancelled) => true,
        (OrderStatus.OutForDelivery, OrderStatus.Delivered) => true,
        _ => false
    };
}

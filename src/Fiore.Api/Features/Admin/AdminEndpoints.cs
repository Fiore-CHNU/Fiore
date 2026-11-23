using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Features.Orders;
using Fiore.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Admin;

public sealed record AdminProductImageRequest(string ImageUrl, string? AltText = null);
public sealed record AdminCompositionRequest(string FlowerName, short StemCount, string? Note = null);
public sealed record CreateCatalogFacetRequest(ProductFacetKind Kind, string Slug, string Name);
public sealed record CreateProductRequest(
    string Sku,
    string Name,
    decimal BasePrice,
    int QuantityAvailable,
    string? Description = null,
    int ReorderLevel = 0,
    string Currency = "UAH",
    ProductStatus Status = ProductStatus.Available,
    IReadOnlyList<AdminProductImageRequest>? Images = null,
    decimal? HeightCm = null,
    decimal? WidthCm = null,
    IReadOnlyList<AdminCompositionRequest>? Composition = null,
    IReadOnlyList<Guid>? FacetIds = null);
public sealed record UpdateProductRequest(
    string Name,
    decimal BasePrice,
    int QuantityAvailable,
    string? Description = null,
    int ReorderLevel = 0,
    string Currency = "UAH",
    ProductStatus Status = ProductStatus.Available,
    IReadOnlyList<AdminProductImageRequest>? Images = null,
    decimal? HeightCm = null,
    decimal? WidthCm = null,
    IReadOnlyList<AdminCompositionRequest>? Composition = null,
    IReadOnlyList<Guid>? FacetIds = null);

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
        RuleFor(request => request.HeightCm).GreaterThan(0).LessThan(1000).When(request => request.HeightCm.HasValue);
        RuleFor(request => request.WidthCm).GreaterThan(0).LessThan(1000).When(request => request.WidthCm.HasValue);
        RuleForEach(request => request.Images).ChildRules(image =>
        {
            image.RuleFor(value => value.ImageUrl).NotEmpty().MaximumLength(2048).Must(value => Uri.IsWellFormedUriString(value, UriKind.Absolute))
                .WithMessage("Image URL must be an absolute URI.");
            image.RuleFor(value => value.AltText).MaximumLength(250).When(value => value.AltText is not null);
        });
        RuleForEach(request => request.Composition).ChildRules(item =>
        {
            item.RuleFor(value => value.FlowerName).NotEmpty().MaximumLength(120);
            item.RuleFor(value => value.StemCount).InclusiveBetween((short)1, (short)200);
            item.RuleFor(value => value.Note).MaximumLength(250).When(value => value.Note is not null);
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
        RuleFor(request => request.HeightCm).GreaterThan(0).LessThan(1000).When(request => request.HeightCm.HasValue);
        RuleFor(request => request.WidthCm).GreaterThan(0).LessThan(1000).When(request => request.WidthCm.HasValue);
        RuleForEach(request => request.Images).ChildRules(image =>
        {
            image.RuleFor(value => value.ImageUrl).NotEmpty().MaximumLength(2048).Must(value => Uri.IsWellFormedUriString(value, UriKind.Absolute));
            image.RuleFor(value => value.AltText).MaximumLength(250).When(value => value.AltText is not null);
        });
        RuleForEach(request => request.Composition).ChildRules(item =>
        {
            item.RuleFor(value => value.FlowerName).NotEmpty().MaximumLength(120);
            item.RuleFor(value => value.StemCount).InclusiveBetween((short)1, (short)200);
            item.RuleFor(value => value.Note).MaximumLength(250).When(value => value.Note is not null);
        });
    }
}

public sealed record UpdateOrderStatusRequest(OrderStatus Status);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin").RequireAuthorization("Staff");

        group.MapPost("/catalog/facets", async (CreateCatalogFacetRequest request, FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var slug = request.Slug.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(slug) || slug.Length > 80 || !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9-]+$") || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["facet"] = ["Kind, a lowercase slug, and a name up to 120 characters are required."] });
            }
            if (await dbContext.CatalogFacets.AnyAsync(facet => facet.Kind == request.Kind && facet.Slug == slug, cancellationToken)) return Results.Conflict(new { error = "FacetAlreadyExists" });
            var facet = new CatalogFacet { Id = Guid.NewGuid(), Kind = request.Kind, Slug = slug, Name = request.Name.Trim() };
            dbContext.CatalogFacets.Add(facet);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/catalog/facets/{facet.Id}", new { facet.Id, facet.Kind, facet.Slug, facet.Name });
        }).WithName("CreateCatalogFacet").WithOpenApi();

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
            var facets = await LoadFacets(dbContext, request.FacetIds, cancellationToken);
            if (facets is null) return Results.BadRequest(new { error = "UnknownFacet" });
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = sku,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                BasePrice = request.BasePrice,
                Currency = request.Currency.Trim().ToUpperInvariant(),
                Status = request.Status,
                HeightCm = request.HeightCm,
                WidthCm = request.WidthCm,
                CreatedAt = now,
                UpdatedAt = now,
                Inventory = new ProductInventory
                {
                    QuantityAvailable = request.QuantityAvailable,
                    ReorderLevel = request.ReorderLevel,
                    UpdatedAt = now
                },
                Images = MapImages(request.Images, now),
                Composition = MapComposition(request.Composition),
                Facets = facets.Select(facet => new ProductFacet { FacetId = facet.Id, Facet = facet }).ToList()
            };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/v1/catalog/products/{product.Id}", ToResponse(product));
        }).WithName("CreateProduct").WithOpenApi();

        group.MapPut("/products/{productId:guid}", async (Guid productId, UpdateProductRequest request, IValidator<UpdateProductRequest> validator, FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var product = await dbContext.Products.Include(candidate => candidate.Inventory).Include(candidate => candidate.Images).Include(candidate => candidate.Composition).Include(candidate => candidate.Facets)
                .SingleOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken);
            if (product is null) return Results.NotFound();

            var now = DateTimeOffset.UtcNow;
            var facets = await LoadFacets(dbContext, request.FacetIds, cancellationToken);
            if (facets is null) return Results.BadRequest(new { error = "UnknownFacet" });
            product.Name = request.Name.Trim();
            product.Description = request.Description?.Trim();
            product.BasePrice = request.BasePrice;
            product.Currency = request.Currency.Trim().ToUpperInvariant();
            product.Status = request.Status;
            product.HeightCm = request.HeightCm;
            product.WidthCm = request.WidthCm;
            product.UpdatedAt = now;
            product.Inventory ??= new ProductInventory { ProductId = product.Id };
            product.Inventory.QuantityAvailable = request.QuantityAvailable;
            product.Inventory.ReorderLevel = request.ReorderLevel;
            product.Inventory.UpdatedAt = now;
            dbContext.ProductImages.RemoveRange(product.Images);
            product.Images = MapImages(request.Images, now);
            dbContext.ProductCompositionItems.RemoveRange(product.Composition);
            product.Composition = MapComposition(request.Composition);
            dbContext.ProductFacets.RemoveRange(product.Facets);
            product.Facets = facets.Select(facet => new ProductFacet { ProductId = product.Id, FacetId = facet.Id, Facet = facet }).ToList();
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToResponse(product));
        }).WithName("UpdateProduct").WithOpenApi();

        group.MapGet("/orders", async (OrderStatus? status, FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var query = dbContext.Orders.AsNoTracking().Include(order => order.Delivery).Include(order => order.Payment).AsQueryable();
            if (status.HasValue) query = query.Where(order => order.Status == status.Value);
            var orders = await query
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
                    payment = new { order.Payment.Status, order.Payment.Provider },
                    delivery = new { order.Delivery.City, order.Delivery.ScheduledFrom, order.Delivery.ScheduledTo }
                }).ToListAsync(cancellationToken);
            return Results.Ok(orders);
        }).WithName("ListAdminOrders").WithOpenApi();

        group.MapPut("/orders/{orderId:guid}/status", async (Guid orderId, UpdateOrderStatusRequest request, OrderLifecycleService lifecycleService, CancellationToken cancellationToken) =>
        {
            var result = await lifecycleService.TransitionByStaff(orderId, request.Status, cancellationToken);
            if (result.Error == OrderTransitionError.NotFound) return Results.NotFound();
            if (result.Error == OrderTransitionError.InvalidTransition)
            {
                return Results.Conflict(new { error = "InvalidOrderStatusTransition" });
            }
            var order = result.Order!;
            return Results.Ok(new { order.Id, order.Status, order.UpdatedAt });
        }).WithName("UpdateOrderStatus").WithOpenApi();

        group.MapPost("/orders/expire-unpaid", async (IConfiguration configuration, OrderLifecycleService lifecycleService, CancellationToken cancellationToken) =>
        {
            var lifetimeMinutes = Math.Max(1, configuration.GetValue("Orders:UnpaidLifetimeMinutes", 30));
            var expired = await lifecycleService.ExpireUnpaidOrders(DateTimeOffset.UtcNow.AddMinutes(-lifetimeMinutes), cancellationToken);
            return Results.Ok(new { expired });
        }).WithName("ExpireUnpaidOrders").WithOpenApi();

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

    private static List<ProductCompositionItem> MapComposition(IReadOnlyList<AdminCompositionRequest>? composition) =>
        (composition ?? []).Select((item, index) => new ProductCompositionItem
        {
            Id = Guid.NewGuid(),
            FlowerName = item.FlowerName.Trim(),
            StemCount = item.StemCount,
            Note = item.Note?.Trim(),
            SortOrder = checked((short)index)
        }).ToList();

    private static async Task<List<CatalogFacet>?> LoadFacets(FioreDbContext dbContext, IReadOnlyList<Guid>? facetIds, CancellationToken cancellationToken)
    {
        var ids = (facetIds ?? []).Distinct().ToList();
        var facets = await dbContext.CatalogFacets.Where(facet => ids.Contains(facet.Id)).ToListAsync(cancellationToken);
        return facets.Count == ids.Count ? facets : null;
    }

    private static object ToResponse(Product product) => new
    {
        product.Id,
        product.Sku,
        product.Name,
        product.Description,
        product.BasePrice,
        product.Currency,
        product.Status,
        product.HeightCm,
        product.WidthCm,
        quantityAvailable = product.Inventory?.QuantityAvailable ?? 0,
        reorderLevel = product.Inventory?.ReorderLevel ?? 0,
        images = product.Images.OrderBy(image => image.SortOrder).Select(image => new { image.Id, image.ImageUrl, image.AltText, image.SortOrder }),
        composition = product.Composition.OrderBy(item => item.SortOrder).Select(item => new { item.FlowerName, item.StemCount, item.Note, item.SortOrder }),
        facetIds = product.Facets.Select(link => link.FacetId)
    };

}

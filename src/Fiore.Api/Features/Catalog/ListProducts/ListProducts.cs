using Fiore.Api.Domain.Catalog;
using Fiore.Api.Infrastructure.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Catalog.ListProducts;

public enum CatalogSort
{
    Newest,
    PriceAscending,
    PriceDescending
}

public sealed record ListProductsRequest(
    int Page = 1,
    int PageSize = 12,
    string? Search = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool InStockOnly = false,
    CatalogSort Sort = CatalogSort.Newest) : IRequest<ListProductsResponse>;

public sealed class ListProductsRequestValidator : AbstractValidator<ListProductsRequest>
{
    public ListProductsRequestValidator()
    {
        RuleFor(request => request.Page).GreaterThanOrEqualTo(1);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request.Search).MaximumLength(100).When(request => request.Search is not null);
        RuleFor(request => request.MinPrice).GreaterThanOrEqualTo(0).When(request => request.MinPrice.HasValue);
        RuleFor(request => request.MaxPrice).GreaterThanOrEqualTo(0).When(request => request.MaxPrice.HasValue);
        RuleFor(request => request).Must(request => !request.MinPrice.HasValue || !request.MaxPrice.HasValue || request.MinPrice <= request.MaxPrice)
            .WithMessage("MinPrice must not exceed MaxPrice.");
    }
}

public sealed class ListProductsHandler(FioreDbContext dbContext) : IRequestHandler<ListProductsRequest, ListProductsResponse>
{
    public async Task<ListProductsResponse> Handle(ListProductsRequest request, CancellationToken cancellationToken)
    {
        IQueryable<Product> products = dbContext.Products
            .AsNoTracking()
            .Where(product => product.Status == ProductStatus.Available);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            products = products.Where(product => product.Name.ToLower().Contains(search) || product.Sku.ToLower().Contains(search));
        }

        if (request.MinPrice.HasValue)
        {
            products = products.Where(product => product.BasePrice >= request.MinPrice.Value);
        }

        if (request.MaxPrice.HasValue)
        {
            products = products.Where(product => product.BasePrice <= request.MaxPrice.Value);
        }

        if (request.InStockOnly)
        {
            products = products.Where(product => product.Inventory != null && product.Inventory.QuantityAvailable > 0);
        }

        var totalItems = await products.CountAsync(cancellationToken);
        var orderedProducts = request.Sort switch
        {
            CatalogSort.PriceAscending => products.OrderBy(product => product.BasePrice).ThenBy(product => product.Id),
            CatalogSort.PriceDescending => products.OrderByDescending(product => product.BasePrice).ThenBy(product => product.Id),
            _ => products.OrderByDescending(product => product.CreatedAt).ThenBy(product => product.Id)
        };

        var items = await orderedProducts
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(product => new CatalogProductItem(
                product.Id,
                product.Sku,
                product.Name,
                product.BasePrice,
                product.Currency,
                product.Images.OrderBy(image => image.SortOrder).Select(image => image.ImageUrl).FirstOrDefault(),
                product.Inventory != null && product.Inventory.QuantityAvailable > 0))
            .ToListAsync(cancellationToken);

        return new ListProductsResponse(
            items,
            new PaginationMetadata(
                request.Page,
                request.PageSize,
                totalItems,
                (int)Math.Ceiling(totalItems / (double)request.PageSize)));
    }
}

public sealed record CatalogProductItem(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    string? ImageUrl,
    bool IsAvailable);

public sealed record PaginationMetadata(int Page, int PageSize, int TotalItems, int TotalPages);

public sealed record ListProductsResponse(IReadOnlyList<CatalogProductItem> Items, PaginationMetadata Pagination);

public static class ListProductsEndpoint
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/catalog/products", async (
            [AsParameters] ListProductsRequest request,
            IValidator<ListProductsRequest> validator,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);

            if (!validation.IsValid)
            {
                var errors = validation.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).ToArray());

                return Results.ValidationProblem(errors);
            }

            return Results.Ok(await sender.Send(request, cancellationToken));
        })
        .WithName("ListCatalogProducts")
        .WithOpenApi();

        return endpoints;
    }
}

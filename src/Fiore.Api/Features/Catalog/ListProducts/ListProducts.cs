using Fiore.Api.Domain.Catalog;
using Fiore.Api.Infrastructure.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Catalog.ListProducts;

public sealed record ListProductsRequest(int Page = 1, int PageSize = 12) : IRequest<ListProductsResponse>;

public sealed class ListProductsRequestValidator : AbstractValidator<ListProductsRequest>
{
    public ListProductsRequestValidator()
    {
        RuleFor(request => request.Page).GreaterThanOrEqualTo(1);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ListProductsHandler(FioreDbContext dbContext) : IRequestHandler<ListProductsRequest, ListProductsResponse>
{
    public async Task<ListProductsResponse> Handle(ListProductsRequest request, CancellationToken cancellationToken)
    {
        var products = dbContext.Products
            .AsNoTracking()
            .Where(product => product.Status == ProductStatus.Available);

        var totalItems = await products.CountAsync(cancellationToken);
        var items = await products
            .OrderByDescending(product => product.CreatedAt)
            .ThenBy(product => product.Id)
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

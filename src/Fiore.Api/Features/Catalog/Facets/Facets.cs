using Fiore.Api.Domain.Catalog;
using Fiore.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Catalog.Facets;

public sealed record CatalogFacetResponse(Guid Id, ProductFacetKind Kind, string Slug, string Name);

public static class FacetEndpoints
{
    public static IEndpointRouteBuilder MapFacetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/catalog/facets", async (ProductFacetKind? kind, FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var query = dbContext.CatalogFacets.AsNoTracking();
            if (kind.HasValue) query = query.Where(facet => facet.Kind == kind.Value);
            var facets = await query.OrderBy(facet => facet.Kind).ThenBy(facet => facet.Name)
                .Select(facet => new CatalogFacetResponse(facet.Id, facet.Kind, facet.Slug, facet.Name))
                .ToListAsync(cancellationToken);
            return Results.Ok(facets);
        }).WithName("ListCatalogFacets").CacheOutput("Catalog").WithOpenApi();

        return endpoints;
    }
}

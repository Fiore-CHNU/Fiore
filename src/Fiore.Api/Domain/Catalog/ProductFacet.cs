namespace Fiore.Api.Domain.Catalog;

public enum ProductFacetKind
{
    Occasion,
    FlowerType,
    Color
}

public sealed class CatalogFacet
{
    public Guid Id { get; set; }
    public ProductFacetKind Kind { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public List<ProductFacet> Products { get; set; } = [];
}

public sealed class ProductFacet
{
    public Guid ProductId { get; set; }
    public Guid FacetId { get; set; }
    public Product Product { get; set; } = null!;
    public CatalogFacet Facet { get; set; } = null!;
}

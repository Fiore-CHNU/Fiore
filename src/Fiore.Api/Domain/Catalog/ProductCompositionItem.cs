namespace Fiore.Api.Domain.Catalog;

public sealed class ProductCompositionItem
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string FlowerName { get; set; } = null!;
    public short StemCount { get; set; }
    public string? Note { get; set; }
    public short SortOrder { get; set; }
    public Product Product { get; set; } = null!;
}

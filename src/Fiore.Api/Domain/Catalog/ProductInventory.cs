namespace Fiore.Api.Domain.Catalog;

public sealed class ProductInventory
{
    public Guid ProductId { get; set; }
    public int QuantityAvailable { get; set; }
    public int ReorderLevel { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Product Product { get; set; } = null!;
}

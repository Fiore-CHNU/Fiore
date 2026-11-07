namespace Fiore.Api.Domain.Catalog;

public sealed class Product
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public string Currency { get; set; } = "UAH";
    public ProductStatus Status { get; set; } = ProductStatus.Available;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ProductImage> Images { get; set; } = [];
    public ProductInventory? Inventory { get; set; }
}

namespace Fiore.Api.Domain.Catalog;

public sealed class ProductImage
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ImageUrl { get; set; } = null!;
    public string? AltText { get; set; }
    public short SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Product Product { get; set; } = null!;
}

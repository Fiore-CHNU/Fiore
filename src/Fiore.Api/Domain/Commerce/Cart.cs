using Fiore.Api.Domain.Catalog;

namespace Fiore.Api.Domain.Commerce;

public sealed class Cart
{
    public Guid Id { get; set; }
    public Guid SessionToken { get; set; }
    public CartStatus Status { get; set; } = CartStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CartItem> Items { get; set; } = [];
}

public sealed class CartItem
{
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public Cart Cart { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

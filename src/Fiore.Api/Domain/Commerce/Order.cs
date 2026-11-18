using Fiore.Api.Domain.Catalog;

namespace Fiore.Api.Domain.Commerce;

public sealed class Order
{
    public Guid Id { get; set; }
    public Guid SourceCartId { get; set; }
    public string ContactName { get; set; } = null!;
    public string ContactEmail { get; set; } = null!;
    public string ContactPhone { get; set; } = null!;
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public string Currency { get; set; } = "UAH";
    public decimal SubtotalAmount { get; set; }
    public decimal DeliveryFeeAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? CustomerNote { get; set; }
    public string? CardMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public OrderDelivery Delivery { get; set; } = null!;
}

public sealed class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string ProductSku { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public sealed class OrderDelivery
{
    public Guid OrderId { get; set; }
    public string RecipientName { get; set; } = null!;
    public string RecipientPhone { get; set; } = null!;
    public string City { get; set; } = null!;
    public string Street { get; set; } = null!;
    public string Building { get; set; } = null!;
    public string? Apartment { get; set; }
    public string? DeliveryNotes { get; set; }
    public DateTimeOffset ScheduledFrom { get; set; }
    public DateTimeOffset ScheduledTo { get; set; }
    public Order Order { get; set; } = null!;
}

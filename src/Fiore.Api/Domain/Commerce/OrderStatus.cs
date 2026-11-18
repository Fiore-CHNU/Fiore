namespace Fiore.Api.Domain.Commerce;

public enum OrderStatus
{
    PendingPayment,
    Paid,
    Accepted,
    Assembling,
    ReadyForDelivery,
    OutForDelivery,
    Delivered,
    Cancelled
}

using Fiore.Api.Domain.Commerce;
using Fiore.Api.Features.Common;
using Fiore.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Orders;

/// <summary>
/// Public order-status view opened from the confirmation link (US-6.2). It deliberately omits contact details,
/// address, and phone numbers (NFR-09), because anyone holding the link can open it.
/// </summary>
public sealed record TrackedOrderResponse(
    string OrderNumber,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ScheduledFrom,
    DateTimeOffset ScheduledTo,
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<TrackedOrderItemResponse> Items,
    IReadOnlyList<TrackedOrderStatusResponse> StatusHistory);

public sealed record TrackedOrderItemResponse(string Name, int Quantity);
public sealed record TrackedOrderStatusResponse(OrderStatus Status, DateTimeOffset ChangedAt);

public static class TrackOrderEndpoints
{
    public static IEndpointRouteBuilder MapTrackOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/orders/track/{trackingCode}", async (string trackingCode, FioreDbContext dbContext, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(trackingCode) || trackingCode.Length > 64) return Results.NotFound();

            var order = await dbContext.Orders
                .AsNoTracking()
                .Include(candidate => candidate.Items)
                .Include(candidate => candidate.Delivery)
                .Include(candidate => candidate.StatusHistory)
                .SingleOrDefaultAsync(candidate => candidate.TrackingCode == trackingCode, cancellationToken);
            if (order?.Delivery is null) return Results.NotFound();

            return Results.Ok(new TrackedOrderResponse(
                CommerceRules.OrderNumber(order.Id),
                order.Status,
                order.CreatedAt,
                order.Delivery.ScheduledFrom,
                order.Delivery.ScheduledTo,
                order.TotalAmount,
                order.Currency,
                order.Items.Select(item => new TrackedOrderItemResponse(item.ProductName, item.Quantity)).ToList(),
                order.StatusHistory.OrderBy(history => history.ChangedAt).Select(history => new TrackedOrderStatusResponse(history.Status, history.ChangedAt)).ToList()));
        }).WithName("TrackOrder").WithOpenApi();

        return endpoints;
    }
}

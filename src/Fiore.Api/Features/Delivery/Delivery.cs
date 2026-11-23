using Fiore.Api.Domain.Commerce;
using Fiore.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Delivery;

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";
    public decimal FlatFeeUah { get; init; } = 120;
    public decimal FreeDeliveryFromUah { get; init; } = 1_500;
    public int SlotCapacity { get; init; } = 10;
}

public sealed record DeliverySlotResponse(DateTimeOffset ScheduledFrom, DateTimeOffset ScheduledTo, int RemainingCapacity, decimal FeeAmount, string Currency);

public sealed class DeliverySlotService(FioreDbContext dbContext, DeliveryOptions options)
{
    private static readonly OrderStatus[] CapacityStatuses =
    [
        OrderStatus.PendingPayment,
        OrderStatus.Paid,
        OrderStatus.Accepted,
        OrderStatus.Assembling,
        OrderStatus.ReadyForDelivery,
        OrderStatus.OutForDelivery
    ];

    public decimal CalculateFee(decimal subtotal) => subtotal >= options.FreeDeliveryFromUah ? 0 : options.FlatFeeUah;

    public bool IsConfiguredSlot(DateTimeOffset from, DateTimeOffset to) =>
        to == from.AddHours(2) && from.TimeOfDay >= TimeSpan.FromHours(10) && to.TimeOfDay <= TimeSpan.FromHours(20) && from.Minute == 0;

    public async Task<bool> HasCapacity(Guid? excludedOrderId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var booked = await dbContext.OrderDeliveries.CountAsync(delivery =>
            delivery.ScheduledFrom == from &&
            delivery.ScheduledTo == to &&
            CapacityStatuses.Contains(delivery.Order.Status) &&
            (!excludedOrderId.HasValue || delivery.OrderId != excludedOrderId.Value), cancellationToken);
        return booked < options.SlotCapacity;
    }

    public async Task<IReadOnlyList<DeliverySlotResponse>> GetSlots(DateOnly date, CancellationToken cancellationToken)
    {
        var slots = Enumerable.Range(10, 5)
            .Select(hour => CreateSlot(date, hour))
            .ToList();
        var starts = slots.Select(slot => slot.From).ToArray();
        var bookedCounts = await dbContext.OrderDeliveries
            .Where(delivery => starts.Contains(delivery.ScheduledFrom) && CapacityStatuses.Contains(delivery.Order.Status))
            .GroupBy(delivery => delivery.ScheduledFrom)
            .Select(group => new { ScheduledFrom = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ScheduledFrom, item => item.Count, cancellationToken);

        return slots.Select(slot => new DeliverySlotResponse(
            slot.From,
            slot.To,
            Math.Max(0, options.SlotCapacity - bookedCounts.GetValueOrDefault(slot.From)),
            options.FlatFeeUah,
            "UAH")).ToList();
    }

    private static (DateTimeOffset From, DateTimeOffset To) CreateSlot(DateOnly date, int hour)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0));
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
        var from = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        return (from, from.AddHours(2));
    }
}

public static class DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/delivery/slots", async (DateOnly? date, DeliverySlotService service, CancellationToken cancellationToken) =>
        {
            var selectedDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
            if (selectedDate < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = ["Delivery date cannot be in the past."] });
            }

            return Results.Ok(await service.GetSlots(selectedDate, cancellationToken));
        }).WithName("ListDeliverySlots").WithOpenApi();

        return endpoints;
    }
}

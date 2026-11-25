using Fiore.Api.Domain.Commerce;
using Fiore.Api.Features.Common;
using Fiore.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Delivery;

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    /// <summary>Delivery fee for an items subtotal between the minimum order and the free-delivery threshold (US-3.2).</summary>
    public decimal FlatFeeUah { get; init; } = 100;
    public decimal FreeDeliveryFromUah { get; init; } = 1_500;

    /// <summary>Minimum items subtotal for checkout (SRS, BR-5).</summary>
    public decimal MinimumOrderUah { get; init; } = 500;
    public int SlotCapacity { get; init; } = 10;

    /// <summary>A slot can be booked no later than this many minutes before it starts (BR-7).</summary>
    public int BookingBufferMinutes { get; init; } = 90;

    /// <summary>Cities served by delivery (US-4.2: no intercity delivery). Comparison ignores case.</summary>
    public string[]? ServiceCities { get; init; }

    public IReadOnlyList<string> EffectiveServiceCities => ServiceCities is { Length: > 0 } ? ServiceCities : ["Чернівці", "Chernivtsi"];

    public bool IsServedCity(string? city) =>
        city is not null && EffectiveServiceCities.Any(served => string.Equals(served.Trim(), city.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record DeliverySlotResponse(DateTimeOffset ScheduledFrom, DateTimeOffset ScheduledTo, int RemainingCapacity, bool IsAvailable, decimal FeeAmount, string Currency);
public sealed record DeliverySlotsResponse(DateOnly Date, IReadOnlyList<DeliverySlotResponse> Slots);

public sealed class DeliverySlotService(FioreDbContext dbContext, DeliveryOptions options, TimeProvider timeProvider)
{
    private const int FirstSlotHour = 10;
    private const int SlotCount = 5;
    private const int SlotLengthHours = 2;

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

    public bool MeetsMinimumOrder(decimal subtotal) => subtotal >= options.MinimumOrderUah;

    public DateOnly TodayInKyiv() => DateOnly.FromDateTime(CommerceRules.ToKyiv(timeProvider.GetUtcNow()).DateTime);

    /// <summary>Slots are 10:00–12:00 … 18:00–20:00 Kyiv time (BR-6), regardless of the offset the client sent.</summary>
    public bool IsConfiguredSlot(DateTimeOffset from, DateTimeOffset to)
    {
        if (to - from != TimeSpan.FromHours(SlotLengthHours)) return false;
        var local = CommerceRules.ToKyiv(from);
        return local.Minute == 0 && local.Second == 0 &&
            local.Hour >= FirstSlotHour &&
            local.Hour <= FirstSlotHour + (SlotCount - 1) * SlotLengthHours &&
            (local.Hour - FirstSlotHour) % SlotLengthHours == 0;
    }

    public bool IsBookableInTime(DateTimeOffset from) => from - timeProvider.GetUtcNow() >= TimeSpan.FromMinutes(options.BookingBufferMinutes);

    public async Task<bool> HasCapacity(Guid? excludedOrderId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        var booked = await dbContext.OrderDeliveries.CountAsync(delivery =>
            delivery.ScheduledFrom == from &&
            delivery.ScheduledTo == to &&
            CapacityStatuses.Contains(delivery.Order.Status) &&
            (!excludedOrderId.HasValue || delivery.OrderId != excludedOrderId.Value), cancellationToken);
        return booked < options.SlotCapacity;
    }

    public async Task<IReadOnlyList<DeliverySlotResponse>> GetSlots(DateOnly date, CancellationToken cancellationToken)
    {
        var slots = Enumerable.Range(0, SlotCount)
            .Select(index => CreateSlot(date, FirstSlotHour + index * SlotLengthHours))
            .ToList();
        var starts = slots.Select(slot => slot.From).ToArray();
        var bookedCounts = await dbContext.OrderDeliveries
            .Where(delivery => starts.Contains(delivery.ScheduledFrom) && CapacityStatuses.Contains(delivery.Order.Status))
            .GroupBy(delivery => delivery.ScheduledFrom)
            .Select(group => new { ScheduledFrom = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ScheduledFrom, item => item.Count, cancellationToken);

        return slots.Select(slot =>
        {
            var remaining = Math.Max(0, options.SlotCapacity - bookedCounts.GetValueOrDefault(slot.From));
            return new DeliverySlotResponse(slot.From, slot.To, remaining, remaining > 0 && IsBookableInTime(slot.From), options.FlatFeeUah, "UAH");
        }).ToList();
    }

    private static (DateTimeOffset From, DateTimeOffset To) CreateSlot(DateOnly date, int hour)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0));
        // Npgsql only writes UTC DateTimeOffset values to timestamptz, so slot boundaries are kept in UTC.
        var from = new DateTimeOffset(local, CommerceRules.KyivTimeZone.GetUtcOffset(local)).ToUniversalTime();
        return (from, from.AddHours(SlotLengthHours));
    }
}

public static class DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/delivery/slots", async (DateOnly? date, DeliverySlotService service, CancellationToken cancellationToken) =>
        {
            var today = service.TodayInKyiv();
            if (date < today)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = ["Delivery date cannot be in the past."] });
            }

            var selectedDate = date ?? today;
            var slots = await service.GetSlots(selectedDate, cancellationToken);

            // US-4.3 scenario 3: when no date was requested and nothing is left today, offer the next day.
            if (date is null && slots.All(slot => !slot.IsAvailable))
            {
                selectedDate = selectedDate.AddDays(1);
                slots = await service.GetSlots(selectedDate, cancellationToken);
            }

            return Results.Ok(new DeliverySlotsResponse(selectedDate, slots));
        }).WithName("ListDeliverySlots").WithOpenApi();

        return endpoints;
    }
}

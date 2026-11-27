using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Features.Payments;
using Fiore.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Orders;

public sealed class OrderOptions
{
    public const string SectionName = "Orders";

    /// <summary>Unpaid orders are cancelled after this many minutes (US-5.2, BR-9).</summary>
    public int UnpaidLifetimeMinutes { get; init; } = 15;

    /// <summary>The background check runs at least once a minute (US-5.2).</summary>
    public int ExpirationCheckIntervalMinutes { get; init; } = 1;

    public TimeSpan UnpaidLifetime => TimeSpan.FromMinutes(Math.Max(1, UnpaidLifetimeMinutes));
}

public sealed class OrderLifecycleService(
    FioreDbContext dbContext,
    OrderOptions options,
    TimeProvider timeProvider,
    IPaymentGateway paymentGateway,
    ILogger<OrderLifecycleService> logger)
{
    public DateTimeOffset UnpaidCutoff() => timeProvider.GetUtcNow() - options.UnpaidLifetime;

    public async Task<PaymentOperationResult> ConfirmSandboxPayment(
        Guid orderId,
        Guid sessionToken,
        CancellationToken cancellationToken,
        string? simulatedOutcome = null)
    {
        var order = await LoadOrderForGuest(orderId, sessionToken, cancellationToken);
        if (order is null) return PaymentOperationResult.Failure(PaymentError.NotFound);

        // US-5.2 scenario 3: a late payment attempt never revives an expired order, even before the background job runs.
        if (order.Status == OrderStatus.PendingPayment && order.CreatedAt <= UnpaidCutoff())
        {
            Expire(order, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            return PaymentOperationResult.Failure(PaymentError.PaymentWindowExpired);
        }

        if (order.Status == OrderStatus.Cancelled && order.Payment?.Status == PaymentStatus.Expired)
        {
            return PaymentOperationResult.Failure(PaymentError.PaymentWindowExpired);
        }

        if (order.Status != OrderStatus.PendingPayment ||
            order.Payment is null ||
            order.Payment.Status is not (PaymentStatus.Initiated or PaymentStatus.Failed))
        {
            return PaymentOperationResult.Failure(PaymentError.NotPayable);
        }

        PaymentGatewayResult gatewayResult;
        try
        {
            gatewayResult = await paymentGateway.ChargeAsync(
                new PaymentGatewayRequest(
                    order.Id,
                    order.Payment.ProviderPaymentId,
                    order.Payment.Amount,
                    order.Payment.Currency,
                    simulatedOutcome),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Payment provider {Provider} failed while charging order {OrderId}.", paymentGateway.ProviderName, order.Id);
            gatewayResult = PaymentGatewayResult.ProviderUnavailable(
                "Платіжний сервіс тимчасово недоступний. Замовлення збережено, спробуйте повторити оплату.");
        }

        var now = timeProvider.GetUtcNow();
        order.Payment.Provider = paymentGateway.ProviderName;
        order.Payment.UpdatedAt = now;
        order.UpdatedAt = now;

        switch (gatewayResult.Outcome)
        {
            case PaymentGatewayOutcome.Succeeded:
                order.Status = OrderStatus.Paid;
                if (!string.IsNullOrWhiteSpace(gatewayResult.ProviderReference))
                {
                    order.Payment.ProviderPaymentId = gatewayResult.ProviderReference;
                }
                order.Payment.Status = PaymentStatus.Succeeded;
                order.Payment.CompletedAt = now;
                AddHistory(order, OrderStatus.Paid, $"Payment confirmed ({paymentGateway.ProviderName}).", now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return PaymentOperationResult.Success(order.Payment, order.Status);

            case PaymentGatewayOutcome.Declined:
                order.Payment.Status = PaymentStatus.Failed;
                AddHistory(order, OrderStatus.PendingPayment, "Payment declined by provider.", now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return PaymentOperationResult.Failure(PaymentError.PaymentDeclined, gatewayResult.ErrorMessage);

            default:
                order.Payment.Status = PaymentStatus.Failed;
                AddHistory(order, OrderStatus.PendingPayment, "Payment provider unavailable.", now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return PaymentOperationResult.Failure(PaymentError.ProviderUnavailable, gatewayResult.ErrorMessage);
        }
    }

    public async Task<OrderTransitionResult> TransitionByStaff(Guid orderId, OrderStatus nextStatus, string? changedBy, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);
        if (order is null) return OrderTransitionResult.Failure(OrderTransitionError.NotFound);
        if (!IsValidStaffTransition(order.Status, nextStatus)) return OrderTransitionResult.Failure(OrderTransitionError.InvalidTransition);
        if (nextStatus == OrderStatus.OutForDelivery && string.IsNullOrWhiteSpace(order.Delivery?.CourierName))
        {
            return OrderTransitionResult.Failure(OrderTransitionError.CourierRequired);
        }

        // US-9.4 / US-10.1: "out for delivery" requires an assigned courier.
        if (nextStatus == OrderStatus.OutForDelivery && string.IsNullOrWhiteSpace(order.Delivery?.CourierName))
        {
            return OrderTransitionResult.Failure(OrderTransitionError.CourierRequired);
        }

        var now = timeProvider.GetUtcNow();
        if (nextStatus == OrderStatus.Cancelled)
        {
            ReleaseReservedInventory(order, now);
            if (order.Payment is { Status: PaymentStatus.Initiated } payment)
            {
                payment.Status = PaymentStatus.Failed;
                payment.UpdatedAt = now;
            }
        }

        order.Status = nextStatus;
        order.UpdatedAt = now;
        AddHistory(order, nextStatus, nextStatus == OrderStatus.Cancelled ? "Cancelled by staff." : "Status updated by staff.", now, changedBy);
        await dbContext.SaveChangesAsync(cancellationToken);
        return OrderTransitionResult.Success(order);
    }

    public async Task<OrderTransitionResult> AssignCourier(
        Guid orderId,
        Guid? courierId,
        string courierName,
        string? courierPhone,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);
        if (order is null) return OrderTransitionResult.Failure(OrderTransitionError.NotFound);
        if (order.Status is not (OrderStatus.Assembling or OrderStatus.ReadyForDelivery) || order.Delivery is null)
        {
            return OrderTransitionResult.Failure(OrderTransitionError.InvalidStatusForCourier);
        }

        var now = timeProvider.GetUtcNow();
        order.Delivery.CourierId = courierId;
        order.Delivery.CourierName = courierName.Trim();
        order.Delivery.CourierPhone = string.IsNullOrWhiteSpace(courierPhone) ? null : courierPhone.Trim();
        order.Delivery.CourierAssignedAt = now;
        order.UpdatedAt = now;
        AddHistory(order, order.Status, $"Courier assigned: {order.Delivery.CourierName}.", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return OrderTransitionResult.Success(order);
    }

    public static string FormatDeliverySummary(Order order)
    {
        var delivery = order.Delivery;
        var address = string.IsNullOrWhiteSpace(delivery.Apartment)
            ? $"{delivery.City}, {delivery.Street}, {delivery.Building}"
            : $"{delivery.City}, {delivery.Street}, {delivery.Building}, кв. {delivery.Apartment}";
        var slot = $"{delivery.ScheduledFrom:dd.MM.yyyy HH:mm}–{delivery.ScheduledTo:HH:mm}";
        var lines = new List<string>
        {
            $"Замовлення #{order.Id:N}",
            $"Адреса: {address}",
            $"Слот: {slot}",
            $"Одержувач: {delivery.RecipientName} ({delivery.RecipientPhone})"
        };
        if (!string.IsNullOrWhiteSpace(delivery.DeliveryNotes))
        {
            lines.Add($"Примітка: {delivery.DeliveryNotes}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public async Task<int> ExpireUnpaidOrders(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var orders = await dbContext.Orders
            .Include(order => order.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Inventory)
            .Include(order => order.Payment)
            .Where(order => order.Status == OrderStatus.PendingPayment && order.CreatedAt <= cutoff)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var order in orders)
        {
            Expire(order, now);
        }

        if (orders.Count > 0) await dbContext.SaveChangesAsync(cancellationToken);
        return orders.Count;
    }

    private void Expire(Order order, DateTimeOffset now)
    {
        ReleaseReservedInventory(order, now);
        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = now;
        if (order.Payment is not null)
        {
            order.Payment.Status = PaymentStatus.Expired;
            order.Payment.UpdatedAt = now;
        }
        AddHistory(order, OrderStatus.Cancelled, "Unpaid order expired.", now);
    }

    public static bool IsValidStaffTransition(OrderStatus current, OrderStatus next) => (current, next) switch
    {
        (OrderStatus.Paid, OrderStatus.Accepted or OrderStatus.Cancelled) => true,
        (OrderStatus.Accepted, OrderStatus.Assembling or OrderStatus.Cancelled) => true,
        (OrderStatus.Assembling, OrderStatus.ReadyForDelivery or OrderStatus.Cancelled) => true,
        (OrderStatus.ReadyForDelivery, OrderStatus.OutForDelivery or OrderStatus.Cancelled) => true,
        (OrderStatus.OutForDelivery, OrderStatus.Delivered) => true,
        (OrderStatus.PendingPayment, OrderStatus.Cancelled) => true,
        _ => false
    };

    private Task<Order?> LoadOrderForGuest(Guid orderId, Guid sessionToken, CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(order => order.Payment)
            .Include(order => order.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Inventory)
            .SingleOrDefaultAsync(order => order.Id == orderId && dbContext.Carts.Any(cart => cart.Id == order.SourceCartId && cart.SessionToken == sessionToken), cancellationToken);

    private Task<Order?> LoadOrder(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(order => order.Delivery)
            .Include(order => order.Payment)
            .Include(order => order.Delivery)
            .Include(order => order.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Inventory)
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);

    private static void ReleaseReservedInventory(Order order, DateTimeOffset now)
    {
        foreach (var item in order.Items)
        {
            if (item.Product.Inventory is null) continue;
            item.Product.Inventory.QuantityAvailable += item.Quantity;
            item.Product.Inventory.UpdatedAt = now;
        }
    }

    private void AddHistory(Order order, OrderStatus status, string note, DateTimeOffset now, string? changedBy = null) =>
        dbContext.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Status = status,
            Note = note,
            ChangedBy = changedBy,
            ChangedAt = now
        });
}

public enum PaymentError { NotFound, NotPayable, PaymentWindowExpired, PaymentDeclined, ProviderUnavailable }
public sealed record PaymentOperationResult(Payment? Payment, OrderStatus? OrderStatus, PaymentError? Error, string? ErrorMessage = null)
{
    public static PaymentOperationResult Success(Payment payment, OrderStatus orderStatus) => new(payment, orderStatus, null);
    public static PaymentOperationResult Failure(PaymentError error, string? errorMessage = null) => new(null, null, error, errorMessage);
}

public enum OrderTransitionError { NotFound, InvalidTransition, CourierRequired, InvalidStatusForCourier }
public sealed record OrderTransitionResult(Order? Order, OrderTransitionError? Error)
{
    public static OrderTransitionResult Success(Order order) => new(order, null);
    public static OrderTransitionResult Failure(OrderTransitionError error) => new(null, error);
}

public sealed class UnpaidOrderExpirationService(IServiceScopeFactory scopeFactory, OrderOptions options, ILogger<UnpaidOrderExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.ExpirationCheckIntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<OrderLifecycleService>();
                var expired = await lifecycle.ExpireUnpaidOrders(lifecycle.UnpaidCutoff(), stoppingToken);
                if (expired > 0)
                {
                    // Restored stock must be visible in the cached catalogue immediately.
                    await scope.ServiceProvider.GetRequiredService<IOutputCacheStore>().EvictByTagAsync("catalog", stoppingToken);
                    logger.LogInformation("Expired {ExpiredOrderCount} unpaid Fiore orders.", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to expire unpaid Fiore orders.");
            }
        }
    }
}

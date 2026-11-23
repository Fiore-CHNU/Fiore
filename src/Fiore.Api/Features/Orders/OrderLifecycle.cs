using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Features.Orders;

public sealed class OrderLifecycleService(FioreDbContext dbContext)
{
    public async Task<PaymentOperationResult> ConfirmSandboxPayment(Guid orderId, Guid sessionToken, CancellationToken cancellationToken)
    {
        var order = await LoadOrderForGuest(orderId, sessionToken, cancellationToken);
        if (order is null) return PaymentOperationResult.Failure(PaymentError.NotFound);
        if (order.Status != OrderStatus.PendingPayment || order.Payment is null || order.Payment.Status != PaymentStatus.Initiated)
        {
            return PaymentOperationResult.Failure(PaymentError.NotPayable);
        }

        var now = DateTimeOffset.UtcNow;
        order.Status = OrderStatus.Paid;
        order.UpdatedAt = now;
        order.Payment.Status = PaymentStatus.Succeeded;
        order.Payment.CompletedAt = now;
        order.Payment.UpdatedAt = now;
        AddHistory(order, OrderStatus.Paid, "Sandbox payment confirmed.", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return PaymentOperationResult.Success(order.Payment, order.Status);
    }

    public async Task<OrderTransitionResult> TransitionByStaff(Guid orderId, OrderStatus nextStatus, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);
        if (order is null) return OrderTransitionResult.Failure(OrderTransitionError.NotFound);
        if (!IsValidStaffTransition(order.Status, nextStatus)) return OrderTransitionResult.Failure(OrderTransitionError.InvalidTransition);

        var now = DateTimeOffset.UtcNow;
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
        AddHistory(order, nextStatus, nextStatus == OrderStatus.Cancelled ? "Cancelled by staff." : "Status updated by staff.", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return OrderTransitionResult.Success(order);
    }

    public async Task<int> ExpireUnpaidOrders(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var orders = await dbContext.Orders
            .Include(order => order.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Inventory)
            .Include(order => order.Payment)
            .Where(order => order.Status == OrderStatus.PendingPayment && order.CreatedAt <= cutoff)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var order in orders)
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

        if (orders.Count > 0) await dbContext.SaveChangesAsync(cancellationToken);
        return orders.Count;
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
            .Include(order => order.Payment)
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

    private void AddHistory(Order order, OrderStatus status, string note, DateTimeOffset now) =>
        dbContext.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Status = status,
            Note = note,
            ChangedAt = now
        });
}

public enum PaymentError { NotFound, NotPayable }
public sealed record PaymentOperationResult(Payment? Payment, OrderStatus? OrderStatus, PaymentError? Error)
{
    public static PaymentOperationResult Success(Payment payment, OrderStatus orderStatus) => new(payment, orderStatus, null);
    public static PaymentOperationResult Failure(PaymentError error) => new(null, null, error);
}

public enum OrderTransitionError { NotFound, InvalidTransition }
public sealed record OrderTransitionResult(Order? Order, OrderTransitionError? Error)
{
    public static OrderTransitionResult Success(Order order) => new(order, null);
    public static OrderTransitionResult Failure(OrderTransitionError error) => new(null, error);
}

public sealed class UnpaidOrderExpirationService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<UnpaidOrderExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Max(1, configuration.GetValue("Orders:ExpirationCheckIntervalMinutes", 5));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var lifetimeMinutes = Math.Max(1, configuration.GetValue("Orders:UnpaidLifetimeMinutes", 30));
                var expired = await scope.ServiceProvider.GetRequiredService<OrderLifecycleService>()
                    .ExpireUnpaidOrders(DateTimeOffset.UtcNow.AddMinutes(-lifetimeMinutes), stoppingToken);
                if (expired > 0) logger.LogInformation("Expired {ExpiredOrderCount} unpaid Fiore orders.", expired);
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

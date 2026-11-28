using Fiore.Api.Domain.Commerce;
using Fiore.Api.Features.Common;
using Fiore.Api.Features.Delivery;
using Fiore.Api.Features.Orders;
using FluentAssertions;

namespace Fiore.Api.Tests.Features.BusinessRules;

/// <summary>Unit tests for business rules from the Jira acceptance criteria, without HTTP or a database.</summary>
public sealed class BusinessRuleTests
{
    private static readonly DeliveryOptions Options = new();

    // DeliverySlotService fee and minimum-order rules do not use the database.
    private static readonly DeliverySlotService Delivery = new(null!, Options, TimeProvider.System);

    [Theory]
    [InlineData(500, 120)]
    [InlineData(1499.99, 120)]
    [InlineData(1500, 0)]
    [InlineData(2650, 0)]
    public void US_3_2_delivery_fee_is_free_from_1500(decimal subtotal, decimal expectedFee)
    {
        Delivery.CalculateFee(subtotal).Should().Be(expectedFee);
    }

    [Theory]
    [InlineData(499.99, false)]
    [InlineData(500, true)]
    public void US_3_2_minimum_order_is_500(decimal subtotal, bool expected)
    {
        Delivery.MeetsMinimumOrder(subtotal).Should().Be(expected);
    }

    [Theory]
    [InlineData("+380501112233", true)]
    [InlineData(" +380501112233 ", true)]
    [InlineData("0501112233", false)]
    [InlineData("+38050111223", false)]
    [InlineData("+3805011122334", false)]
    [InlineData(null, false)]
    public void US_4_1_phone_must_be_plus_380_and_nine_digits(string? phone, bool expected)
    {
        CommerceRules.IsValidPhone(phone).Should().Be(expected);
    }

    [Theory]
    [InlineData("Чернівці", true)]
    [InlineData("chernivtsi", true)]
    [InlineData("Київ", false)]
    public void US_4_2_delivery_only_within_the_city(string city, bool expected)
    {
        Options.IsServedCity(city).Should().Be(expected);
    }

    [Theory]
    [InlineData(OrderStatus.Paid, OrderStatus.Accepted, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.OutForDelivery, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.OutForDelivery, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    public void US_9_4_staff_can_only_move_one_step_forward_or_cancel_before_dispatch(OrderStatus current, OrderStatus next, bool expected)
    {
        OrderLifecycleService.IsValidStaffTransition(current, next).Should().Be(expected);
    }

    [Fact]
    public void US_6_2_tracking_codes_are_random_and_url_safe()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => CommerceRules.NewTrackingCode()).ToList();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(code => code.Length == 24 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'));
    }
}

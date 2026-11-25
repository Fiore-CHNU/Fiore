using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Fiore.Api.Domain.Catalog;

namespace Fiore.Api.Features.Common;

/// <summary>
/// Business rules shared by several vertical slices. Rule identifiers refer to the Jira EAN backlog.
/// </summary>
public static partial class CommerceRules
{
    public static readonly TimeZoneInfo KyivTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    /// <summary>Ukrainian mobile number in the format +380XXXXXXXXX (US-4.1, US-4.2).</summary>
    public const string PhoneFormatMessage = "Phone must be in the format +380XXXXXXXXX.";

    /// <summary>Greeting-card text limit (US-4.4, BR-2).</summary>
    public const int CardMessageMaxLength = 250;

    public static bool IsValidPhone(string? phone) => phone is not null && PhonePattern().IsMatch(phone.Trim());

    /// <summary>A product can be ordered when it is published, not switched off by staff, and has stock (BR-3, US-8.3).</summary>
    public static readonly Expression<Func<Product, bool>> IsOrderableExpression = product =>
        product.Status == ProductStatus.Available &&
        !product.IsTemporarilyUnavailable &&
        product.Inventory != null &&
        product.Inventory.QuantityAvailable > 0;

    private static readonly Func<Product, bool> IsOrderableCompiled = IsOrderableExpression.Compile();

    public static bool IsOrderable(Product product) => IsOrderableCompiled(product);

    /// <summary>Unguessable code used in the public order-status link (US-6.2, SRS 5.3).</summary>
    public static string NewTrackingCode()
    {
        Span<byte> bytes = stackalloc byte[18];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>Short human-readable order number shown to customers and staff.</summary>
    public static string OrderNumber(Guid orderId) => $"FIO-{orderId.ToString("N")[..8].ToUpperInvariant()}";

    public static DateTimeOffset ToKyiv(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, KyivTimeZone);

    [GeneratedRegex(@"^\+380\d{9}$")]
    private static partial Regex PhonePattern();
}

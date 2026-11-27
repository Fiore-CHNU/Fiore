using Fiore.Api.Features.Catalog.GetProductDetails;
using Fiore.Api.Features.Catalog.ListProducts;
using Fiore.Api.Features.Catalog.Facets;
using Fiore.Api.Features.Carts;
using Fiore.Api.Features.Checkout;
using Fiore.Api.Features.Auth;
using Fiore.Api.Features.Admin;
using Fiore.Api.Features.Delivery;
using Fiore.Api.Features.Orders;
using Fiore.Api.Features.Payments;
using Fiore.Api.Infrastructure.Persistence;
using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Domain.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? (builder.Environment.IsProduction()
        ? throw new InvalidOperationException("JWT signing key is required in production.")
        : "local-development-signing-key-must-not-be-used-in-production-2026");
var jwtOptions = new JwtOptions(
    builder.Configuration["Jwt:Issuer"] ?? "Fiore.Api",
    builder.Configuration["Jwt:Audience"] ?? "Fiore.Client",
    jwtSigningKey,
    builder.Configuration.GetValue("Jwt:ExpiryMinutes", 120));

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is required.");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<ProductStatus>("product_status");
dataSourceBuilder.MapEnum<ProductFacetKind>("product_facet_kind");
dataSourceBuilder.MapEnum<CartStatus>("cart_status");
dataSourceBuilder.MapEnum<OrderStatus>("order_status");
dataSourceBuilder.MapEnum<PaymentStatus>("payment_status");
dataSourceBuilder.MapEnum<UserAccountStatus>("user_account_status");
var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<FioreDbContext>(options => options.UseNpgsql(dataSource));
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton(builder.Configuration.GetSection(DeliveryOptions.SectionName).Get<DeliveryOptions>() ?? new DeliveryOptions());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration.GetSection(OrderOptions.SectionName).Get<OrderOptions>() ?? new OrderOptions());
builder.Services.AddScoped<DeliverySlotService>();
builder.Services.AddScoped<IPaymentGateway, SandboxPaymentGateway>();
builder.Services.AddScoped<OrderLifecycleService>();
builder.Services.AddHostedService<UnpaidOrderExpirationService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization(options => options.AddPolicy("Staff", policy => policy.RequireRole("manager", "administrator")));
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
    if (!builder.Environment.IsEnvironment("Testing") &&
        !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Redis")))
    {
        builder.AddRedisOutputCache("Redis");
    }
    else
    {
        builder.Services.AddOutputCache();
    }
    builder.Services.Configure<OutputCacheOptions>(options =>
    {
        options.AddPolicy("Catalog", policy => policy
            .Expire(TimeSpan.FromMinutes(5))
            .SetVaryByQuery(["page", "pageSize", "search", "minPrice", "maxPrice", "inStockOnly",
            "sort", "occasion", "flowerType", "color", "kind"])
            .Tag("catalog"));
    });

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173", "http://localhost:4173"];

    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod();
}));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<FioreDbContext>();
    if (dbContext.Database.IsRelational())
    {
        await dbContext.Database.MigrateAsync();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        await FioreDbSeeder.SeedAsync(dbContext, passwordHasher);
    }

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapDefaultEndpoints();
app.UseCors("Frontend");
app.UseOutputCache();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("HealthCheck")
    .WithOpenApi();

app.MapCatalogEndpoints();
app.MapCatalogDetailEndpoints();
app.MapFacetEndpoints();
app.MapCartEndpoints();
app.MapCheckoutEndpoints();
app.MapTrackOrderEndpoints();
app.MapDeliveryEndpoints();
app.MapPaymentEndpoints();
app.MapAuthEndpoints();
app.MapAdminEndpoints();

app.Run();

public partial class Program;

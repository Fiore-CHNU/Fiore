using Fiore.Api.Features.Catalog.GetProductDetails;
using Fiore.Api.Features.Catalog.ListProducts;
using Fiore.Api.Features.Carts;
using Fiore.Api.Features.Checkout;
using Fiore.Api.Infrastructure.Persistence;
using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is required.");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<ProductStatus>("product_status");
dataSourceBuilder.MapEnum<CartStatus>("cart_status");
dataSourceBuilder.MapEnum<OrderStatus>("order_status");
var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<FioreDbContext>(options => options.UseNpgsql(dataSource));
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("HealthCheck")
    .WithOpenApi();

app.MapCatalogEndpoints();
app.MapCatalogDetailEndpoints();
app.MapCartEndpoints();
app.MapCheckoutEndpoints();

app.Run();

public partial class Program;

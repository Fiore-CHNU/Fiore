using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Infrastructure.Persistence;

public sealed class FioreDbContext(DbContextOptions<FioreDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductInventory> ProductInventory => Set<ProductInventory>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderDelivery> OrderDeliveries => Set<OrderDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<ProductStatus>(name: "product_status");
        modelBuilder.HasPostgresEnum<CartStatus>(name: "cart_status");
        modelBuilder.HasPostgresEnum<OrderStatus>(name: "order_status");

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Id).HasColumnName("id");
            entity.Property(product => product.Sku).HasColumnName("sku").IsRequired();
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.Property(product => product.Name).HasColumnName("name").IsRequired();
            entity.Property(product => product.Description).HasColumnName("description");
            entity.Property(product => product.BasePrice).HasColumnName("base_price").HasPrecision(12, 2);
            entity.Property(product => product.Currency).HasColumnName("currency").HasMaxLength(3);
            entity.Property(product => product.Status).HasColumnName("status").HasColumnType("product_status");
            entity.Property(product => product.CreatedAt).HasColumnName("created_at");
            entity.Property(product => product.UpdatedAt).HasColumnName("updated_at");
            entity.HasMany(product => product.Images).WithOne(image => image.Product).HasForeignKey(image => image.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(product => product.Inventory).WithOne(inventory => inventory.Product).HasForeignKey<ProductInventory>(inventory => inventory.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("product_images");
            entity.HasKey(image => image.Id);
            entity.Property(image => image.Id).HasColumnName("id");
            entity.Property(image => image.ProductId).HasColumnName("product_id");
            entity.Property(image => image.ImageUrl).HasColumnName("image_url").IsRequired();
            entity.Property(image => image.AltText).HasColumnName("alt_text");
            entity.Property(image => image.SortOrder).HasColumnName("sort_order");
            entity.Property(image => image.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(image => new { image.ProductId, image.SortOrder }).IsUnique();
        });

        modelBuilder.Entity<ProductInventory>(entity =>
        {
            entity.ToTable("product_inventory");
            entity.HasKey(inventory => inventory.ProductId);
            entity.Property(inventory => inventory.ProductId).HasColumnName("product_id");
            entity.Property(inventory => inventory.QuantityAvailable).HasColumnName("quantity_available");
            entity.Property(inventory => inventory.ReorderLevel).HasColumnName("reorder_level");
            entity.Property(inventory => inventory.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("carts");
            entity.HasKey(cart => cart.Id);
            entity.Property(cart => cart.Id).HasColumnName("id");
            entity.Property(cart => cart.SessionToken).HasColumnName("session_token").IsRequired();
            entity.HasIndex(cart => cart.SessionToken).IsUnique();
            entity.Property(cart => cart.Status).HasColumnName("status").HasColumnType("cart_status");
            entity.Property(cart => cart.CreatedAt).HasColumnName("created_at");
            entity.Property(cart => cart.UpdatedAt).HasColumnName("updated_at");
            entity.HasMany(cart => cart.Items).WithOne(item => item.Cart).HasForeignKey(item => item.CartId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("cart_items");
            entity.HasKey(item => new { item.CartId, item.ProductId });
            entity.Property(item => item.CartId).HasColumnName("cart_id");
            entity.Property(item => item.ProductId).HasColumnName("product_id");
            entity.Property(item => item.Quantity).HasColumnName("quantity");
            entity.Property(item => item.AddedAt).HasColumnName("added_at");
            entity.HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Id).HasColumnName("id");
            entity.Property(order => order.SourceCartId).HasColumnName("source_cart_id");
            entity.HasIndex(order => order.SourceCartId).IsUnique();
            entity.HasOne<Cart>().WithOne().HasForeignKey<Order>(order => order.SourceCartId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(order => order.ContactName).HasColumnName("contact_name").IsRequired();
            entity.Property(order => order.ContactEmail).HasColumnName("contact_email").IsRequired();
            entity.Property(order => order.ContactPhone).HasColumnName("contact_phone").IsRequired();
            entity.Property(order => order.Status).HasColumnName("status").HasColumnType("order_status");
            entity.Property(order => order.Currency).HasColumnName("currency").HasMaxLength(3);
            entity.Property(order => order.SubtotalAmount).HasColumnName("subtotal_amount").HasPrecision(12, 2);
            entity.Property(order => order.DeliveryFeeAmount).HasColumnName("delivery_fee_amount").HasPrecision(12, 2);
            entity.Property(order => order.TotalAmount).HasColumnName("total_amount").HasPrecision(12, 2);
            entity.Property(order => order.CustomerNote).HasColumnName("customer_note");
            entity.Property(order => order.CardMessage).HasColumnName("card_message");
            entity.Property(order => order.CreatedAt).HasColumnName("created_at");
            entity.Property(order => order.UpdatedAt).HasColumnName("updated_at");
            entity.HasMany(order => order.Items).WithOne(item => item.Order).HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(order => order.Delivery).WithOne(delivery => delivery.Order).HasForeignKey<OrderDelivery>(delivery => delivery.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.OrderId).HasColumnName("order_id");
            entity.Property(item => item.ProductId).HasColumnName("product_id");
            entity.Property(item => item.ProductName).HasColumnName("product_name").IsRequired();
            entity.Property(item => item.ProductSku).HasColumnName("product_sku").IsRequired();
            entity.Property(item => item.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 2);
            entity.Property(item => item.Quantity).HasColumnName("quantity");
            entity.Property(item => item.LineTotal).HasColumnName("line_total").HasPrecision(12, 2);
            entity.HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderDelivery>(entity =>
        {
            entity.ToTable("order_deliveries");
            entity.HasKey(delivery => delivery.OrderId);
            entity.Property(delivery => delivery.OrderId).HasColumnName("order_id");
            entity.Property(delivery => delivery.RecipientName).HasColumnName("recipient_name").IsRequired();
            entity.Property(delivery => delivery.RecipientPhone).HasColumnName("recipient_phone").IsRequired();
            entity.Property(delivery => delivery.City).HasColumnName("city").IsRequired();
            entity.Property(delivery => delivery.Street).HasColumnName("street").IsRequired();
            entity.Property(delivery => delivery.Building).HasColumnName("building").IsRequired();
            entity.Property(delivery => delivery.Apartment).HasColumnName("apartment");
            entity.Property(delivery => delivery.DeliveryNotes).HasColumnName("delivery_notes");
            entity.Property(delivery => delivery.ScheduledFrom).HasColumnName("scheduled_from");
            entity.Property(delivery => delivery.ScheduledTo).HasColumnName("scheduled_to");
            entity.HasIndex(delivery => delivery.ScheduledFrom);
        });
    }
}

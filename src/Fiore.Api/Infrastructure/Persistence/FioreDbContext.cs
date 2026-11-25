using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Fiore.Api.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Infrastructure.Persistence;

public sealed class FioreDbContext(DbContextOptions<FioreDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductInventory> ProductInventory => Set<ProductInventory>();
    public DbSet<ProductCompositionItem> ProductCompositionItems => Set<ProductCompositionItem>();
    public DbSet<CatalogFacet> CatalogFacets => Set<CatalogFacet>();
    public DbSet<ProductFacet> ProductFacets => Set<ProductFacet>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderDelivery> OrderDeliveries => Set<OrderDelivery>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<ProductStatus>(name: "product_status");
        modelBuilder.HasPostgresEnum<ProductFacetKind>(name: "product_facet_kind");
        modelBuilder.HasPostgresEnum<CartStatus>(name: "cart_status");
        modelBuilder.HasPostgresEnum<OrderStatus>(name: "order_status");
        modelBuilder.HasPostgresEnum<PaymentStatus>(name: "payment_status");
        modelBuilder.HasPostgresEnum<UserAccountStatus>(name: "user_account_status");

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id");
            entity.Property(user => user.Email).HasColumnName("email").IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(user => user.FirstName).HasColumnName("first_name").IsRequired();
            entity.Property(user => user.LastName).HasColumnName("last_name");
            entity.Property(user => user.Phone).HasColumnName("phone");
            entity.Property(user => user.Status).HasColumnName("status").HasColumnType("user_account_status");
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at");
            entity.HasMany(user => user.Roles).WithOne(role => role.User).HasForeignKey(role => role.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Id).HasColumnName("id");
            entity.Property(role => role.Code).HasColumnName("code").IsRequired();
            entity.HasIndex(role => role.Code).IsUnique();
            entity.Property(role => role.Name).HasColumnName("name").IsRequired();
            entity.HasData(
                new AppRole { Id = 1, Code = "administrator", Name = "Administrator" },
                new AppRole { Id = 2, Code = "manager", Name = "Manager" },
                new AppRole { Id = 3, Code = "customer", Name = "Customer" });
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(role => new { role.UserId, role.RoleId });
            entity.Property(role => role.UserId).HasColumnName("user_id");
            entity.Property(role => role.RoleId).HasColumnName("role_id");
            entity.Property(role => role.AssignedAt).HasColumnName("assigned_at");
            entity.HasOne(role => role.Role).WithMany(role => role.Users).HasForeignKey(role => role.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Id).HasColumnName("id");
            entity.Property(product => product.Sku).HasColumnName("sku").IsRequired();
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.HasIndex(product => new { product.Status, product.CreatedAt, product.Id });
            entity.HasIndex(product => new { product.Status, product.BasePrice, product.Id });
            entity.Property(product => product.Name).HasColumnName("name").IsRequired();
            entity.Property(product => product.Description).HasColumnName("description");
            entity.Property(product => product.BasePrice).HasColumnName("base_price").HasPrecision(12, 2);
            entity.Property(product => product.Currency).HasColumnName("currency").HasMaxLength(3);
            entity.Property(product => product.Status).HasColumnName("status").HasColumnType("product_status");
            entity.Property(product => product.CreatedAt).HasColumnName("created_at");
            entity.Property(product => product.UpdatedAt).HasColumnName("updated_at");
            entity.Property(product => product.HeightCm).HasColumnName("height_cm").HasPrecision(6, 2);
            entity.Property(product => product.WidthCm).HasColumnName("width_cm").HasPrecision(6, 2);
            entity.HasMany(product => product.Images).WithOne(image => image.Product).HasForeignKey(image => image.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(product => product.Inventory).WithOne(inventory => inventory.Product).HasForeignKey<ProductInventory>(inventory => inventory.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(product => product.Composition).WithOne(item => item.Product).HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(product => product.Facets).WithOne(link => link.Product).HasForeignKey(link => link.ProductId).OnDelete(DeleteBehavior.Cascade);
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
            entity.HasIndex(inventory => inventory.QuantityAvailable);
            entity.Property(inventory => inventory.ReorderLevel).HasColumnName("reorder_level");
            entity.Property(inventory => inventory.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ProductCompositionItem>(entity =>
        {
            entity.ToTable("product_composition_items");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.ProductId).HasColumnName("product_id");
            entity.Property(item => item.FlowerName).HasColumnName("flower_name").HasMaxLength(120).IsRequired();
            entity.Property(item => item.StemCount).HasColumnName("stem_count");
            entity.Property(item => item.Note).HasColumnName("note").HasMaxLength(250);
            entity.Property(item => item.SortOrder).HasColumnName("sort_order");
            entity.HasIndex(item => new { item.ProductId, item.SortOrder }).IsUnique();
        });

        modelBuilder.Entity<CatalogFacet>(entity =>
        {
            entity.ToTable("catalog_facets");
            entity.HasKey(facet => facet.Id);
            entity.Property(facet => facet.Id).HasColumnName("id");
            entity.Property(facet => facet.Kind).HasColumnName("kind").HasColumnType("product_facet_kind");
            entity.Property(facet => facet.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
            entity.Property(facet => facet.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            entity.HasIndex(facet => new { facet.Kind, facet.Slug }).IsUnique();
        });

        modelBuilder.Entity<ProductFacet>(entity =>
        {
            entity.ToTable("product_facets");
            entity.HasKey(link => new { link.ProductId, link.FacetId });
            entity.Property(link => link.ProductId).HasColumnName("product_id");
            entity.Property(link => link.FacetId).HasColumnName("facet_id");
            entity.HasOne(link => link.Facet).WithMany(facet => facet.Products).HasForeignKey(link => link.FacetId).OnDelete(DeleteBehavior.Restrict);
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
            entity.HasOne(order => order.Payment).WithOne(payment => payment.Order).HasForeignKey<Payment>(payment => payment.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(order => order.StatusHistory).WithOne(history => history.Order).HasForeignKey(history => history.OrderId).OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.Id).HasColumnName("id");
            entity.Property(payment => payment.OrderId).HasColumnName("order_id");
            entity.HasIndex(payment => payment.OrderId).IsUnique();
            entity.Property(payment => payment.Provider).HasColumnName("provider").HasMaxLength(64).IsRequired();
            entity.Property(payment => payment.ProviderPaymentId).HasColumnName("provider_payment_id").HasMaxLength(128).IsRequired();
            entity.HasIndex(payment => new { payment.Provider, payment.ProviderPaymentId }).IsUnique();
            entity.Property(payment => payment.Status).HasColumnName("status").HasColumnType("payment_status");
            entity.Property(payment => payment.Amount).HasColumnName("amount").HasPrecision(12, 2);
            entity.Property(payment => payment.Currency).HasColumnName("currency").HasMaxLength(3);
            entity.Property(payment => payment.CreatedAt).HasColumnName("created_at");
            entity.Property(payment => payment.UpdatedAt).HasColumnName("updated_at");
            entity.Property(payment => payment.CompletedAt).HasColumnName("completed_at");
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.ToTable("order_status_history");
            entity.HasKey(history => history.Id);
            entity.Property(history => history.Id).HasColumnName("id");
            entity.Property(history => history.OrderId).HasColumnName("order_id");
            entity.Property(history => history.Status).HasColumnName("status").HasColumnType("order_status");
            entity.Property(history => history.Note).HasColumnName("note").HasMaxLength(500);
            entity.Property(history => history.ChangedAt).HasColumnName("changed_at");
            entity.HasIndex(history => new { history.OrderId, history.ChangedAt });
        });
    }
}

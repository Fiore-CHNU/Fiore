using Fiore.Api.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Infrastructure.Persistence;

public sealed class FioreDbContext(DbContextOptions<FioreDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductInventory> ProductInventory => Set<ProductInventory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<ProductStatus>(name: "product_status");

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
    }
}

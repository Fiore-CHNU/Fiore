using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiore.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_products_status_base_price_id",
                table: "products",
                columns: new[] { "status", "base_price", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_products_status_created_at_id",
                table: "products",
                columns: new[] { "status", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_product_inventory_quantity_available",
                table: "product_inventory",
                column: "quantity_available");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_status_base_price_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_status_created_at_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_product_inventory_quantity_available",
                table: "product_inventory");
        }
    }
}

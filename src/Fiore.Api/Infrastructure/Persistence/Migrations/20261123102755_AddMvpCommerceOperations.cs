using System;
using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Commerce;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiore.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMvpCommerceOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:cart_status", "active,converted,abandoned")
                .Annotation("Npgsql:Enum:order_status", "pending_payment,paid,accepted,assembling,ready_for_delivery,out_for_delivery,delivered,cancelled")
                .Annotation("Npgsql:Enum:payment_status", "initiated,succeeded,failed,expired")
                .Annotation("Npgsql:Enum:product_facet_kind", "occasion,flower_type,color")
                .Annotation("Npgsql:Enum:product_status", "available,hidden,discontinued")
                .Annotation("Npgsql:Enum:user_account_status", "active,blocked")
                .OldAnnotation("Npgsql:Enum:cart_status", "active,converted,abandoned")
                .OldAnnotation("Npgsql:Enum:order_status", "pending_payment,paid,accepted,assembling,ready_for_delivery,out_for_delivery,delivered,cancelled")
                .OldAnnotation("Npgsql:Enum:product_status", "available,hidden,discontinued")
                .OldAnnotation("Npgsql:Enum:user_account_status", "active,blocked");

            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                table: "products",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width_cm",
                table: "products",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "catalog_facets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<ProductFacetKind>(type: "product_facet_kind", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_facets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<OrderStatus>(type: "order_status", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_status_history_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_payment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<PaymentStatus>(type: "payment_status", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.ForeignKey(
                        name: "FK_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_composition_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flower_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    stem_count = table.Column<short>(type: "smallint", nullable: false),
                    note = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_composition_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_product_composition_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_facets",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facet_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_facets", x => new { x.product_id, x.facet_id });
                    table.ForeignKey(
                        name: "FK_product_facets_catalog_facets_facet_id",
                        column: x => x.facet_id,
                        principalTable: "catalog_facets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_facets_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_facets_kind_slug",
                table: "catalog_facets",
                columns: new[] { "kind", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_status_history_order_id_changed_at",
                table: "order_status_history",
                columns: new[] { "order_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_order_id",
                table: "payments",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_provider_provider_payment_id",
                table: "payments",
                columns: new[] { "provider", "provider_payment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_composition_items_product_id_sort_order",
                table: "product_composition_items",
                columns: new[] { "product_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_facets_facet_id",
                table: "product_facets",
                column: "facet_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_status_history");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "product_composition_items");

            migrationBuilder.DropTable(
                name: "product_facets");

            migrationBuilder.DropTable(
                name: "catalog_facets");

            migrationBuilder.DropColumn(
                name: "height_cm",
                table: "products");

            migrationBuilder.DropColumn(
                name: "width_cm",
                table: "products");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:cart_status", "active,converted,abandoned")
                .Annotation("Npgsql:Enum:order_status", "pending_payment,paid,accepted,assembling,ready_for_delivery,out_for_delivery,delivered,cancelled")
                .Annotation("Npgsql:Enum:product_status", "available,hidden,discontinued")
                .Annotation("Npgsql:Enum:user_account_status", "active,blocked")
                .OldAnnotation("Npgsql:Enum:cart_status", "active,converted,abandoned")
                .OldAnnotation("Npgsql:Enum:order_status", "pending_payment,paid,accepted,assembling,ready_for_delivery,out_for_delivery,delivered,cancelled")
                .OldAnnotation("Npgsql:Enum:payment_status", "initiated,succeeded,failed,expired")
                .OldAnnotation("Npgsql:Enum:product_facet_kind", "occasion,flower_type,color")
                .OldAnnotation("Npgsql:Enum:product_status", "available,hidden,discontinued")
                .OldAnnotation("Npgsql:Enum:user_account_status", "active,blocked");
        }
    }
}

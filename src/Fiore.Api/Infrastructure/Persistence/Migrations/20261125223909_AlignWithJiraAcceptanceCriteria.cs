using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiore.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignWithJiraAcceptanceCriteria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_temporarily_unavailable",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "packaging",
                table: "products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_code",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "changed_by",
                table: "order_status_history",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "call_before_delivery",
                table: "order_deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                table: "cart_items",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "failed_login_count",
                table: "app_users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_ends_at",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill existing rows: every order needs its own unguessable status-link code, and carts keep
            // the price currently shown so that no existing cart is reported as repriced.
            migrationBuilder.Sql("UPDATE orders SET tracking_code = replace(gen_random_uuid()::text, '-', '') || replace(gen_random_uuid()::text, '-', '') WHERE tracking_code = '';");
            migrationBuilder.Sql("UPDATE cart_items AS ci SET unit_price = p.base_price FROM products AS p WHERE p.id = ci.product_id;");

            migrationBuilder.CreateIndex(
                name: "IX_orders_tracking_code",
                table: "orders",
                column: "tracking_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_tracking_code",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "is_temporarily_unavailable",
                table: "products");

            migrationBuilder.DropColumn(
                name: "packaging",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tracking_code",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "changed_by",
                table: "order_status_history");

            migrationBuilder.DropColumn(
                name: "call_before_delivery",
                table: "order_deliveries");

            migrationBuilder.DropColumn(
                name: "unit_price",
                table: "cart_items");

            migrationBuilder.DropColumn(
                name: "failed_login_count",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "lockout_ends_at",
                table: "app_users");
        }
    }
}

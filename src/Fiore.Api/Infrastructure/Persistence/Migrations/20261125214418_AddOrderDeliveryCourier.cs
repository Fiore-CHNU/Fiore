using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiore.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDeliveryCourier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "courier_assigned_at",
                table: "order_deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "courier_id",
                table: "order_deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "courier_name",
                table: "order_deliveries",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "courier_phone",
                table: "order_deliveries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "courier_assigned_at",
                table: "order_deliveries");

            migrationBuilder.DropColumn(
                name: "courier_id",
                table: "order_deliveries");

            migrationBuilder.DropColumn(
                name: "courier_name",
                table: "order_deliveries");

            migrationBuilder.DropColumn(
                name: "courier_phone",
                table: "order_deliveries");
        }
    }
}

using System;
using Fiore.Api.Domain.Identity;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Fiore.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:cart_status", "active,converted,abandoned")
                .Annotation("Npgsql:Enum:order_status", "pending_payment,paid,accepted,assembling,ready_for_delivery,out_for_delivery,delivered,cancelled")
                .Annotation("Npgsql:Enum:product_status", "available,hidden,discontinued")
                .Annotation("Npgsql:Enum:user_account_status", "active,blocked")
                .OldAnnotation("Npgsql:Enum:cart_status", "active,converted,abandoned")
                .OldAnnotation("Npgsql:Enum:order_status", "pending_payment,paid,accepted,assembling,ready_for_delivery,out_for_delivery,delivered,cancelled")
                .OldAnnotation("Npgsql:Enum:product_status", "available,hidden,discontinued");

            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<UserAccountStatus>(type: "user_account_status", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<short>(type: "smallint", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_user_roles_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "code", "name" },
                values: new object[,]
                {
                    { (short)1, "administrator", "Administrator" },
                    { (short)2, "manager", "Manager" },
                    { (short)3, "customer", "Customer" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_users_email",
                table: "app_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roles_code",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_role_id",
                table: "user_roles",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "app_users");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:cart_status", "active,converted,abandoned")
                .Annotation("Npgsql:Enum:order_status", "pending_payment,paid,accepted,assembling,ready_for_delivery,out_for_delivery,delivered,cancelled")
                .Annotation("Npgsql:Enum:product_status", "available,hidden,discontinued")
                .OldAnnotation("Npgsql:Enum:cart_status", "active,converted,abandoned")
                .OldAnnotation("Npgsql:Enum:order_status", "pending_payment,paid,accepted,assembling,ready_for_delivery,out_for_delivery,delivered,cancelled")
                .OldAnnotation("Npgsql:Enum:product_status", "available,hidden,discontinued")
                .OldAnnotation("Npgsql:Enum:user_account_status", "active,blocked");
        }
    }
}

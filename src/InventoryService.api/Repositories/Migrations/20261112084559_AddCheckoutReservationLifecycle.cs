using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.api.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutReservationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutId",
                table: "Reservations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectionAvailableQuantity",
                table: "Reservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionCode",
                table: "Reservations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectionProductId",
                table: "Reservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Reservations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectionRequestedQuantity",
                table: "Reservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Reservations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                EXEC(N'UPDATE [Reservations]
                    SET [Status] = CASE WHEN [IsReleased] = 1 THEN 2 WHEN [IsConfirmed] = 1 THEN 1 ELSE 0 END;');
                """);

            migrationBuilder.DropColumn(name: "IsConfirmed", table: "Reservations");
            migrationBuilder.DropColumn(name: "IsReleased", table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CheckoutId",
                table: "Reservations",
                column: "CheckoutId",
                unique: true,
                filter: "[CheckoutId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_CheckoutId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CheckoutId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RejectionAvailableQuantity",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RejectionCode",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RejectionProductId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RejectionRequestedQuantity",
                table: "Reservations");

            migrationBuilder.AddColumn<bool>(
                name: "IsConfirmed",
                table: "Reservations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReleased",
                table: "Reservations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                EXEC(N'UPDATE [Reservations]
                    SET [IsConfirmed] = CASE WHEN [Status] = 1 THEN 1 ELSE 0 END,
                        [IsReleased] = CASE WHEN [Status] IN (2, 3) THEN 1 ELSE 0 END;');
                """);

            migrationBuilder.DropColumn(name: "Status", table: "Reservations");
        }
    }
}

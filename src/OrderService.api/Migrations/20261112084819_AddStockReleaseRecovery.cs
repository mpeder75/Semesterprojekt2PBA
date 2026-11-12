using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReleaseRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecoveryStatus",
                table: "CheckoutProcesses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CheckoutProcesses",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StockRecoveryDueAt",
                table: "CheckoutProcesses",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecoveryStatus",
                table: "CheckoutProcesses");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CheckoutProcesses");

            migrationBuilder.DropColumn(
                name: "StockRecoveryDueAt",
                table: "CheckoutProcesses");
        }
    }
}

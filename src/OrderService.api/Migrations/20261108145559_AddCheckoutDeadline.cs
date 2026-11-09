using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.api.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadlineAt",
                table: "CheckoutProcesses",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeadlineAt",
                table: "CheckoutProcesses");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.api.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutProcess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckoutProcesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    BasketId = table.Column<int>(type: "int", nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ShipToAddress_Street = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ShipToAddress_City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShipToAddress_State = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ShipToAddress_Country = table.Column<string>(type: "nvarchar(90)", maxLength: 90, nullable: false),
                    ShipToAddress_ZipCode = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutProcesses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutProcesses_CheckoutId",
                table: "CheckoutProcesses",
                column: "CheckoutId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutProcesses");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.api.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutLineSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PictureUri",
                table: "CheckoutLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "CheckoutLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "CheckoutLine",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PictureUri",
                table: "CheckoutLine");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "CheckoutLine");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "CheckoutLine");
        }
    }
}

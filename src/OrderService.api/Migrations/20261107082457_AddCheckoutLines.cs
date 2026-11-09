using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.api.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckoutLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CheckoutProcessId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutLine_CheckoutProcesses_CheckoutProcessId",
                        column: x => x.CheckoutProcessId,
                        principalTable: "CheckoutProcesses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutLine_CheckoutProcessId",
                table: "CheckoutLine",
                column: "CheckoutProcessId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutLine");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.api.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReservedQuantity",
                table: "InventoryItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    IsReleased = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.OrderId);
                    table.CheckConstraint("CK_Reservations_OrderId", "[OrderId] > 0");
                });

            migrationBuilder.CreateTable(
                name: "ReservationItems",
                columns: table => new
                {
                    CatalogItemId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationItems", x => new { x.OrderId, x.CatalogItemId });
                    table.CheckConstraint("CK_ReservationItems_CatalogItemId", "[CatalogItemId] > 0");
                    table.CheckConstraint("CK_ReservationItems_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReservationItems_Reservations_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Reservations",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryItems_ReservedQuantity",
                table: "InventoryItems",
                sql: "[ReservedQuantity] >= 0 AND [ReservedQuantity] <= [Quantity]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationItems");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryItems_ReservedQuantity",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "ReservedQuantity",
                table: "InventoryItems");
        }
    }
}

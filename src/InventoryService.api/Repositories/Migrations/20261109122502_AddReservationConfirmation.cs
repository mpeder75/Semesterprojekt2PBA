using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.api.Repositories.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsConfirmed",
                table: "Reservations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsConfirmed",
                table: "Reservations");
        }
    }
}

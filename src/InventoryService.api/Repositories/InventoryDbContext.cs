using InventoryService.api.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.api.Repositories;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(item =>
        {
            item.ToTable("InventoryItems", table =>
            {
                table.HasCheckConstraint("CK_InventoryItems_Quantity", "[Quantity] >= 0");
                table.HasCheckConstraint("CK_InventoryItems_CatalogItemId", "[CatalogItemId] > 0");
                table.HasCheckConstraint("CK_InventoryItems_ReservedQuantity", "[ReservedQuantity] >= 0 AND [ReservedQuantity] <= [Quantity]");
            });
            item.HasKey(x => x.CatalogItemId);
            item.Property(x => x.CatalogItemId).ValueGeneratedNever();
            item.Ignore(x => x.AvailableQuantity);
        });

        modelBuilder.Entity<StockReservation>(reservation =>
        {
            reservation.ToTable("Reservations", table =>
                table.HasCheckConstraint("CK_Reservations_OrderId", "[OrderId] > 0"));
            reservation.HasKey(x => x.OrderId);
            reservation.Property(x => x.OrderId).ValueGeneratedNever();
            // This relationship stays entirely inside Inventory's own database.
            reservation.HasMany(x => x.Items).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReservationItem>(item =>
        {
            item.ToTable("ReservationItems", table =>
            {
                table.HasCheckConstraint("CK_ReservationItems_Quantity", "[Quantity] > 0");
                table.HasCheckConstraint("CK_ReservationItems_CatalogItemId", "[CatalogItemId] > 0");
            });
            item.HasKey("OrderId", nameof(ReservationItem.CatalogItemId));
            item.Property(x => x.CatalogItemId).ValueGeneratedNever();
        });
    }
}

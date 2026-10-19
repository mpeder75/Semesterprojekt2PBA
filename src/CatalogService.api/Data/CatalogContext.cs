using System.Reflection;
using CatalogService.api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.api.Data;

public class CatalogContext : DbContext
{
    #pragma waring disable CS8618
    public CatalogContext(DbContextOptions<CatalogContext> options) : base(options) { }

    public DbSet<CatalogItem> CatalogItems { get; set; }
    public DbSet<CatalogBrand> CatalogBrands { get; set; }
    public DbSet<CatalogType> CatalogTypes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}

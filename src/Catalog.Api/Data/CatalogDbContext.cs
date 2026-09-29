using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Catalog.Contracts;

namespace Microsoft.eShopWeb.Catalog.Api.Data;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string PictureUri { get; set; } = "";
    public int CatalogBrandId { get; set; }
    public int CatalogTypeId { get; set; }
    public CatalogItemDto ToDto() => new(Id, Name, Description, Price, PictureUri, CatalogBrandId, CatalogTypeId);
    public void Update(CatalogItemWrite value)
    {
        Name = value.Name;
        Description = value.Description;
        Price = value.Price;
        CatalogBrandId = value.CatalogBrandId;
        CatalogTypeId = value.CatalogTypeId;
    }
}

public class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class ProductType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Items => Set<Product>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<ProductType> Types => Set<ProductType>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Product>(entity =>
        {
            entity.ToTable("Items");
            entity.Property(p => p.Name).HasMaxLength(50).IsRequired();
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.HasOne<Brand>().WithMany().HasForeignKey(p => p.CatalogBrandId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductType>().WithMany().HasForeignKey(p => p.CatalogTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Brand>().ToTable("Brands");
        builder.Entity<ProductType>().ToTable("Types");
    }
}

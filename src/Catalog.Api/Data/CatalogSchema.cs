using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Catalog.Api.Data;

public static class CatalogSchema
{
    public static async Task InitializeAsync(CatalogDbContext db)
    {
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync();
            return;
        }
        if (await db.Database.CanConnectAsync() && !(await db.Database.GetAppliedMigrationsAsync()).Any())
        {
            await db.Database.OpenConnectionAsync();
            try
            {
                using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Items', 'Brands', 'Types')";
                if (Convert.ToInt32(await command.ExecuteScalarAsync()) > 0)
                    throw new InvalidOperationException("This database was created before Catalog migrations. Export it with --export, import into a NEW database with --import, verify the snapshots, then change CatalogDatabase. The original database is preserved.");
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
        await db.Database.MigrateAsync();
    }
}

// Allows schema tooling without starting the API or supplying service credentials.
public class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__CatalogDatabase")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=CatalogDesignTime;Integrated Security=true").Options);
}

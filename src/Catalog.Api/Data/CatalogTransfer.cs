using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Catalog.Contracts;

namespace Microsoft.eShopWeb.Catalog.Api.Data;

public static class CatalogTransfer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<bool> RunAsync(string[] args, IServiceProvider services, IConfiguration configuration)
    {
        var command = args.FirstOrDefault(a => a is "--import" or "--export" or "--export-legacy");
        if (command is null) return false;
        var index = Array.IndexOf(args, command);
        if (index + 1 >= args.Length) throw new ArgumentException("Supply a snapshot file path.");
        var path = args[index + 1];
        if (command == "--export-legacy")
        {
            var connection = configuration.GetConnectionString("LegacyCatalog")
                ?? throw new InvalidOperationException("Set ConnectionStrings__LegacyCatalog for read-only export.");
            await WriteAsync(path, await ReadLegacyAsync(connection));
            return true;
        }
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        if (command == "--export")
            await WriteAsync(path, await SnapshotAsync(db));
        else
        {
            if (!db.Database.IsSqlServer()) throw new InvalidOperationException("Offline import requires persistent SQL Server storage.");
            var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(await File.ReadAllTextAsync(path), Json)
                ?? throw new ArgumentException("Snapshot is empty.");
            await db.Database.EnsureCreatedAsync();
            await ImportAsync(db, snapshot);
        }
        return true;
    }

    private static async Task WriteAsync(string path, CatalogSnapshot snapshot)
    {
        // Never silently overwrite an earlier migration snapshot.
        await using var file = new FileStream(path, FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(file, snapshot, Json);
    }

    public static async Task<CatalogSnapshot> SnapshotAsync(CatalogDbContext db) => new(
        await db.Brands.OrderBy(b => b.Id).Select(b => new CatalogBrandDto(b.Id, b.Name)).ToListAsync(),
        await db.Types.OrderBy(t => t.Id).Select(t => new CatalogTypeDto(t.Id, t.Name)).ToListAsync(),
        (await db.Items.OrderBy(i => i.Id).ToListAsync()).Select(i => i.ToDto()).ToList());

    public static async Task ImportAsync(CatalogDbContext db, CatalogSnapshot snapshot)
    {
        if (await db.Items.AnyAsync() || await db.Brands.AnyAsync() || await db.Types.AnyAsync())
            throw new InvalidOperationException("Import requires empty catalog tables. Existing data will not be overwritten.");
        if (snapshot.Items is null || snapshot.Brands is null || snapshot.Types is null)
            throw new ArgumentException("Snapshot requires items, brands and types.");
        if (snapshot.Items.Select(i => i.Id).Distinct().Count() != snapshot.Items.Count ||
            snapshot.Brands.Select(b => b.Id).Distinct().Count() != snapshot.Brands.Count ||
            snapshot.Types.Select(t => t.Id).Distinct().Count() != snapshot.Types.Count ||
            snapshot.Items.Any(i => i.Id <= 0 || !snapshot.Brands.Any(b => b.Id == i.CatalogBrandId) || !snapshot.Types.Any(t => t.Id == i.CatalogTypeId)) ||
            snapshot.Brands.Any(b => b.Id <= 0) || snapshot.Types.Any(t => t.Id <= 0))
            throw new ArgumentException("Snapshot IDs must be positive, unique, and reference existing brands/types.");

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        await InsertAsync(db, "Brands", snapshot.Brands.Select(b => new Brand { Id = b.Id, Name = b.Brand }));
        await InsertAsync(db, "Types", snapshot.Types.Select(t => new ProductType { Id = t.Id, Name = t.Type }));
        await InsertAsync(db, "Items", snapshot.Items.Select(i => new Product
        {
            Id = i.Id, Name = i.Name, Description = i.Description, Price = i.Price,
            PictureUri = i.PictureUri, CatalogBrandId = i.CatalogBrandId, CatalogTypeId = i.CatalogTypeId
        }));
        if (transaction is not null) await transaction.CommitAsync();
    }

    private static async Task InsertAsync<T>(CatalogDbContext db, string table, IEnumerable<T> rows) where T : class
    {
        // Table names are internal constants, never user input. Preserve IDs referenced by baskets/orders.
        var enable = table switch
        {
            "Brands" => "SET IDENTITY_INSERT [Brands] ON",
            "Types" => "SET IDENTITY_INSERT [Types] ON",
            "Items" => "SET IDENTITY_INSERT [Items] ON",
            _ => throw new ArgumentException("Unknown catalog table.")
        };
        var disable = enable.Replace(" ON", " OFF");
        if (db.Database.IsSqlServer()) await db.Database.ExecuteSqlRawAsync(enable);
        try
        {
            db.Set<T>().AddRange(rows);
            await db.SaveChangesAsync();
        }
        finally
        {
            if (db.Database.IsSqlServer()) await db.Database.ExecuteSqlRawAsync(disable);
        }
    }

    private static async Task<CatalogSnapshot> ReadLegacyAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var brands = new List<CatalogBrandDto>();
        var types = new List<CatalogTypeDto>();
        var items = new List<CatalogItemDto>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Brand FROM CatalogBrands ORDER BY Id; SELECT Id, Type FROM CatalogTypes ORDER BY Id; SELECT Id, Name, Description, Price, PictureUri, CatalogBrandId, CatalogTypeId FROM Catalog ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) brands.Add(new(reader.GetInt32(0), reader.GetString(1)));
        await reader.NextResultAsync();
        while (await reader.ReadAsync()) types.Add(new(reader.GetInt32(0), reader.GetString(1)));
        await reader.NextResultAsync();
        while (await reader.ReadAsync()) items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.GetDecimal(3), reader.IsDBNull(4) ? "" : reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6)));
        return new(brands, types, items);
    }

    public static async Task SeedDemoAsync(CatalogDbContext db)
    {
        if (await db.Items.AnyAsync() || await db.Brands.AnyAsync() || await db.Types.AnyAsync()) return;
        var brands = new[] { "Azure", ".NET", "Visual Studio", "SQL Server", "Other" }
            .Select((name, index) => new CatalogBrandDto(index + 1, name)).ToArray();
        var types = new[] { "Mug", "T-Shirt", "Sheet", "USB Memory Stick" }
            .Select((name, index) => new CatalogTypeDto(index + 1, name)).ToArray();
        var names = new[] { ".NET Bot Black Sweatshirt", ".NET Black & White Mug", "Prism White T-Shirt", ".NET Foundation Sweatshirt",
            "Roslyn Red Sheet", ".NET Blue Sweatshirt", "Roslyn Red T-Shirt", "Kudu Purple Sweatshirt", "Cup<T> White Mug",
            ".NET Foundation Sheet", "Cup<T> Sheet", "Prism White TShirt" };
        int[] brandIds = [2, 2, 5, 2, 5, 2, 5, 5, 5, 2, 2, 5];
        int[] typeIds = [2, 1, 2, 2, 3, 2, 2, 2, 1, 3, 3, 2];
        decimal[] prices = [19.5m, 8.5m, 12, 12, 8.5m, 12, 12, 8.5m, 12, 12, 8.5m, 12];
        await ImportAsync(db, new(brands, types, names.Select((name, index) =>
            new CatalogItemDto(index + 1, name, name, prices[index],
                $"http://catalogbaseurltobereplaced/images/products/{index + 1}.png", brandIds[index], typeIds[index])).ToArray()));
    }
}

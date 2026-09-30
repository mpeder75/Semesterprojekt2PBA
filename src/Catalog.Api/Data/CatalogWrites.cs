using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Catalog.Contracts;

namespace Microsoft.eShopWeb.Catalog.Api.Data;

public static class CatalogWrites
{
    // Only used by disposable in-memory demos/tests. SQL uses a cross-process transaction lock.
    private static readonly SemaphoreSlim DemoLock = new(1, 1);

    public static async Task<IResult> CreateAsync(CatalogItemWrite request, HttpContext context,
        CatalogDbContext db, CancellationToken token)
    {
        var suppliedKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (suppliedKey.Length > 200 || (context.Request.Headers.ContainsKey("Idempotency-Key") && string.IsNullOrWhiteSpace(suppliedKey)))
            return Results.BadRequest(new { error = "Idempotency-Key must contain between 1 and 200 characters." });
        var key = suppliedKey.Length == 0 ? null : Hash(suppliedKey);
        var requestHash = Hash(JsonSerializer.Serialize(request));
        var demo = !db.Database.IsRelational();
        if (demo) await DemoLock.WaitAsync(token);
        try
        {
            await using var transaction = demo ? null : await db.Database.BeginTransactionAsync(token);
            if (key is not null && db.Database.IsSqlServer())
            {
                // Released on commit/rollback; works across multiple Catalog instances.
                var resource = "catalog-create:" + key;
                await db.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @result int;
EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
IF @result < 0 THROW 51000, 'Could not acquire catalog request lock.', 1;", token);
            }
            if (key is not null && await db.Requests.FindAsync(new object[] { key }, token) is { } previous)
            {
                if (previous.RequestHash != requestHash)
                    return Results.UnprocessableEntity(new { error = "Idempotency-Key was already used for a different request." });
                var saved = JsonSerializer.Deserialize<CatalogItemDto>(previous.ResponseJson)!;
                return Results.Created($"/catalog/items/{saved.Id}", saved);
            }

            await ValidateAsync(request, db, token);
            if (await db.Items.AnyAsync(i => i.Name == request.Name, token)) return Results.Conflict();
            var item = new Product { PictureUri = "/images/products/eCatalog-item-default.png" };
            item.Update(request);
            db.Items.Add(item);
            await db.SaveChangesAsync(token);
            if (key is not null)
            {
                db.Requests.Add(new CatalogRequest { Key = key, RequestHash = requestHash, ResponseJson = JsonSerializer.Serialize(item.ToDto()) });
                await db.SaveChangesAsync(token);
            }
            if (transaction is not null) await transaction.CommitAsync(token);
            return Results.Created($"/catalog/items/{item.Id}", item.ToDto());
        }
        finally { if (demo) DemoLock.Release(); }
    }

    public static async Task ValidateAsync(CatalogItemWrite request, CatalogDbContext db, CancellationToken token)
    {
        CatalogValidation.Validate(request);
        if (!await db.Brands.AnyAsync(b => b.Id == request.CatalogBrandId, token) ||
            !await db.Types.AnyAsync(t => t.Id == request.CatalogTypeId, token))
            throw new ArgumentException("Unknown catalog brand or type.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

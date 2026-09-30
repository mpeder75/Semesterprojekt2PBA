using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Catalog.Api.Data;
using Microsoft.eShopWeb.Catalog.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<CatalogDbContext>(options =>
{
    if (builder.Configuration.GetValue<bool>("UseOnlyInMemoryDatabase"))
        options.UseInMemoryDatabase("CatalogService");
    else
        options.UseSqlServer(builder.Configuration.GetConnectionString("CatalogDatabase"));
});
var app = builder.Build();

if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await CatalogSchema.InitializeAsync(scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
    return;
}

// Offline import/export commands exit without starting the HTTP server.
if (await CatalogTransfer.RunAsync(args, app.Services, app.Configuration)) return;

var apiKey = builder.Configuration["CatalogApi:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Set CatalogApi__ApiKey before starting Catalog.Api.");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await CatalogSchema.InitializeAsync(db);
    if (app.Configuration.GetValue<bool>("SeedDemoData")) await CatalogTransfer.SeedDemoAsync(db);
}

app.Use(async (context, next) =>
{
    try { await next(); }
    catch (ArgumentException ex) { await Results.Problem(ex.Message, statusCode: 400).ExecuteAsync(context); }
    catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
    { await Results.Problem("A catalog item with this name already exists.", statusCode: 409).ExecuteAsync(context); }
});

app.MapGet("/health", async (CatalogDbContext db, CancellationToken token) =>
    await db.Database.CanConnectAsync(token) ? Results.Ok(new { status = "Healthy" }) : Results.StatusCode(503));

var catalog = app.MapGroup("/catalog");
catalog.AddEndpointFilter(async (context, next) =>
{
    var supplied = context.HttpContext.Request.Headers["X-Catalog-Key"].ToString();
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(apiKey))
        ? await next(context) : Results.Unauthorized();
});

catalog.MapGet("/items", async (int? pageIndex, int? pageSize, int? brandId, int? typeId, CatalogDbContext db, CancellationToken token) =>
{
    var index = pageIndex ?? 0;
    var size = pageSize ?? 0;
    CatalogValidation.ValidatePage(index, size);
    var query = db.Items.AsNoTracking().Where(i => (!brandId.HasValue || i.CatalogBrandId == brandId) &&
        (!typeId.HasValue || i.CatalogTypeId == typeId));
    var count = await query.CountAsync(token);
    var page = query.OrderBy(i => i.Id).Skip(index * size);
    if (size > 0) page = page.Take(size);
    return Results.Ok(new CatalogPage((await page.ToListAsync(token)).Select(i => i.ToDto()).ToList(), count));
});

catalog.MapGet("/items/{id:int}", async (int id, CatalogDbContext db, CancellationToken token) =>
    await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, token) is { } item
        ? Results.Ok(item.ToDto()) : Results.NotFound());

catalog.MapPost("/items/lookup", async (int[] ids, CatalogDbContext db, CancellationToken token) =>
    Results.Ok((await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).OrderBy(i => i.Id).ToListAsync(token))
        .Select(i => i.ToDto())));

catalog.MapGet("/brands", async (CatalogDbContext db, CancellationToken token) =>
    Results.Ok(await db.Brands.AsNoTracking().OrderBy(b => b.Id).Select(b => new CatalogBrandDto(b.Id, b.Name)).ToListAsync(token)));
catalog.MapGet("/types", async (CatalogDbContext db, CancellationToken token) =>
    Results.Ok(await db.Types.AsNoTracking().OrderBy(t => t.Id).Select(t => new CatalogTypeDto(t.Id, t.Name)).ToListAsync(token)));

catalog.MapPost("/items", CatalogWrites.CreateAsync);

catalog.MapPut("/items/{id:int}", async (int id, CatalogItemWrite request, CatalogDbContext db, CancellationToken token) =>
{
    await CatalogWrites.ValidateAsync(request, db, token);
    var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id, token);
    if (item is null) return Results.NotFound();
    if (await db.Items.AnyAsync(i => i.Id != id && i.Name == request.Name, token)) return Results.Conflict();
    item.Update(request);
    await db.SaveChangesAsync(token);
    return Results.Ok(item.ToDto());
});

catalog.MapDelete("/items/{id:int}", async (int id, CatalogDbContext db, CancellationToken token) =>
{
    var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id, token);
    if (item is null) return Results.NotFound();
    db.Items.Remove(item);
    await db.SaveChangesAsync(token);
    return Results.NoContent();
});

app.Run();

namespace Microsoft.eShopWeb.Catalog.Api { public partial class CatalogApiMarker { } }

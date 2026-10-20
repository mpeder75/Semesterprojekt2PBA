using CatalogService.api;
using CatalogService.api.Data;
using CatalogService.api.Interfaces;
using CatalogService.api.Services;
using Microsoft.EntityFrameworkCore;
using MinimalApi.Endpoint.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpoints();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAutoMapper(typeof(MappingProfile).Assembly);
var catalogSettings = builder.Configuration.Get<CatalogSettings>() ?? new CatalogSettings();
builder.Services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

builder.Services.AddDbContext<CatalogContext>(c =>
{
    var connectionString = builder.Configuration.GetConnectionString("CatalogConnection");
    c.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());
});

var app = builder.Build();

app.Logger.LogInformation("App created...");

app.Logger.LogInformation("Seeding Database...");

using (var scope = app.Services.CreateScope())
{
    var scopedProvider = scope.ServiceProvider;
    try
    {
        var catalogContext  = scopedProvider.GetRequiredService<CatalogContext>();
        await CatalogContextSeed.SeedAsync(catalogContext, app.Logger);
    }
    catch (Exception ex) 
    {
        
    }
}

 // Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

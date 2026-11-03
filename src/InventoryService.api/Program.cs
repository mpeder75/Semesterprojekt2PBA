using InventoryService.api.Endpoints;
using InventoryService.api.Interfaces;
using InventoryService.api.Repositories;
using InventoryService.api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInventoryRepositories(builder.Configuration);
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await app.Services.MigrateInventoryDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapInventoryEndpoints();
app.Run();

public partial class Program { }

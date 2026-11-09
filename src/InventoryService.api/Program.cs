using InventoryService.api.Consumers;
using InventoryService.api.Endpoints;
using InventoryService.api.Interfaces;
using InventoryService.api.Producers;
using InventoryService.api.Repositories;
using InventoryService.api.Services;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInventoryRepositories(builder.Configuration);
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<InventoryCheckoutHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IConnection>(services => new ConnectionFactory
{
    Uri = new Uri(services.GetRequiredService<IConfiguration>()["RabbitMQ:Uri"]!),
    ClientProvidedName = "InventoryService"
}.CreateConnectionAsync().GetAwaiter().GetResult());
builder.Services.AddSingleton<InventoryCheckoutReplyProducer>();
builder.Services.AddHostedService<InventoryCheckoutRequestConsumer>();

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

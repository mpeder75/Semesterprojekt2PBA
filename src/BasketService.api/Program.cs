using Microsoft.EntityFrameworkCore;
using BasketService.api.Data;
using BasketService.api.Interfaces;
using MinimalApi.Endpoint.Extensions;
using BasketService.api.Data.Queries;
using BasketService.api.Logging;
using BasketService.api.Endpoints;
using RabbitMQ.Client;
using BasketService.api.Messaging.Messages;
using BasketService.api.Messaging.Handlers;
using BasketService.api.Messaging.Deserialization;
using BasketService.api.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<BasketContext>(c =>
    {
        var connetionString = builder.Configuration.GetConnectionString("BasketConnection");
        c.UseSqlServer(connetionString, SqlOptions => SqlOptions.EnableRetryOnFailure());
    });

builder.Services.AddSingleton<IConnection>(_ =>
{
    var factory = new ConnectionFactory
    {
        Uri = new Uri(
            builder.Configuration["RabbitMQ:Uri"]
            ??
            "amqp://guest:guest@localhost:5672/"),
        ClientProvidedName = "BasketService"
    };

    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
builder.Services.AddScoped<IBasketService, BasketService.api.Services.BasketService>();
builder.Services.AddScoped<IBasketQueryService, BasketQueryService>();

//MessageHandlers
builder.Services.AddScoped<IBasketMessageHandler<AddItemToBasketMessage>, AddItemToBasketMessageHandler>();
builder.Services.AddScoped<IBasketMessageHandler<DeleteBasketMessage>, DeleteBasketMessageHandler>();
builder.Services.AddScoped<IBasketMessageHandler<SetBasketQuantitiesMessage>, SetBasketQuantitesMessageHandler>();
builder.Services.AddScoped<IBasketMessageHandler<TransferBasketMessage>, TransferBasketMessageHandler>();

//MessageDeserializer
builder.Services.AddSingleton<IBasketMessageDeserializer, AddItemToBasketMessageDeserializer>();
builder.Services.AddSingleton<IBasketMessageDeserializer, DeleteBasketMessageDeserializer>();
builder.Services.AddSingleton<IBasketMessageDeserializer, SetBasketQuantitiesMessageDeserializer>();
builder.Services.AddSingleton<IBasketMessageDeserializer, TransferBasketMessageDeserializer>();
builder.Services.AddSingleton<BasketMessageDeserializerResolver>();

//RabbitMQ
builder.Services.AddHostedService<BasketRabbitMqConsumer>();

builder.Services.AddEndpoints();

var app = builder.Build();

app.Logger.LogInformation("App created...");

app.Logger.LogInformation("Migrating Database...");

using (var scope = app.Services.CreateScope())
{
    var scopedProvider = scope.ServiceProvider;
    try
    {
        var basketContext = scopedProvider.GetRequiredService<BasketContext>();
        await BasketContextSeed.SeedAsync(basketContext, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "An error occurred migrating the DB.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapBasketEndpoints();

app.MapControllers();


app.Run();

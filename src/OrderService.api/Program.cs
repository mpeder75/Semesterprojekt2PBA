using Microsoft.EntityFrameworkCore;
using MinimalApi.Endpoint.Extensions;
using OrderService.api.Consumers;
using OrderService.api.Data;
using OrderService.api.Interfaces;
using OrderService.api.Producers;
using OrderService.api.Services;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// DbContext
builder.Services.AddDbContext<OrderDbContext>(c =>
{
    var connectionString = builder.Configuration.GetConnectionString("OrderConnection");
    c.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());
});

// Repositories
builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

// RabbitMQ
builder.Services.AddSingleton<IConnection>(_ =>
{
    var factory = new ConnectionFactory
    {
        Uri = new Uri(
            builder.Configuration["RabbitMQ:Uri"]
            ??
            "amqp://guest:guest@localhost:5672/"),
        ClientProvidedName = "OrderService"
    };

    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// Consumers
builder.Services.AddHostedService<CreateOrderConsumer>();
builder.Services.AddHostedService<OrderCheckoutReplyConsumer>();

// Producers
builder.Services.AddSingleton<ICheckoutRequestProducer, CheckoutRequestProducer>();

// Services
builder.Services.AddScoped<CheckoutProcessManager>();

builder.Services.AddEndpoints();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var scopedProvider = scope.ServiceProvider;
    try
    {
        var orderContext = scopedProvider.GetRequiredService<OrderDbContext>();
        await OrderContextSeed.SeedAsync(orderContext, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "An error occurred seeding the DB.");
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
app.MapControllers();
app.MapEndpoints();
app.Run();

using CatalogService.api;
using CatalogService.api.Data;
using CatalogService.api.Interfaces;
using CatalogService.api.Services;
using Microsoft.EntityFrameworkCore;
using MinimalApi.Endpoint.Extensions;
using Microsoft.Extensions.FileProviders;
using RabbitMQ.Client;
using CatalogService.api.Messaging.Registry;
using CatalogService.api.Messaging.Messages;
using CatalogService.api.Messaging.Replies;
using CatalogService.api.Messaging.Handlers;
using CatalogService.api.Messaging.Handlers.Adapters;
using CatalogService.api.Messaging.Dispatcher;
using CatalogService.api.Messaging.Deserialization.Resolvers;
using CatalogService.api.Messaging.Serialization;
using CatalogService.api.Messaging.Consumers;
using CatalogService.api.Messaging.Producers;
using CatalogService.api.Logging;
using CatalogService.api.Messaging.Deserialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpoints();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAutoMapper(typeof(MappingProfile).Assembly);
var catalogSettings = builder.Configuration.Get<CatalogSettings>() ?? new CatalogSettings();
builder.Services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));

builder.Services.AddDbContext<CatalogContext>(c =>
{
    var connectionString = builder.Configuration.GetConnectionString("CatalogConnection");
    c.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());
});

//RabbitMQ Connection
builder.Services.AddSingleton<IConnection>(_ =>
{
    var factory = new ConnectionFactory 
    {
        Uri = new Uri(
        builder.Configuration["RabbitMq:Uri"]
        ??
        "amqp://guest:guest@localhost:5672/"),
        ClientProvidedName = "CatalogService"
    };

    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

//Business logic & Data Infastructure
builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped<ICatalogService, CatalogService.api.Services.CatalogService>();
builder.Services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));

//MessageRegistry
builder.Services.AddSingleton<CatalogMessageRegistry>();

//MessageHandlers
builder.Services.AddScoped<ICatalogMessageHandler<GetCatalogItemsByIdsAsyncMessage, GetCatalogItemsByIdsAsyncReply>, GetCatalogItemsByIdsAsyncHandler>();
builder.Services.AddScoped<ICatalogReplyHandlerAdapter, CatalogReplyHandlerAdapter<GetCatalogItemsByIdsAsyncMessage, GetCatalogItemsByIdsAsyncReply>>();

//MessageDispatcher
builder.Services.AddSingleton<ICatalogMessageDispatcher, CatalogMessageDispatcher>();

//MessageDeserialization
builder.Services.AddSingleton<ICatalogMessageDeserializer, CatalogMessageDeserializer>();
builder.Services.AddSingleton<ICatalogMessageDeserializerResolver, CatalogMessageDeserializerResolver>();

//MessageSerialization
builder.Services.AddScoped<ICatalogMessageSerializer, CatalogMessageSerializer>();

//RabbitMQ
builder.Services.AddHostedService<CatalogRabbitMQRequestConsumer>();
builder.Services.AddSingleton<ICatalogRabbitMQReplyProducer, CatalogRabbitMQReplyProducer>();

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
        app.Logger.LogError(ex, "An error occurred seeding the DB.");
    }
}

 // Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    /*app.UseSwaggerUI(options => 
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        options.RoutePrefix = string.Empty;
    });*/
}

app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(app.Environment.ContentRootPath, "images")),
    RequestPath = "/images"
});

app.UseRouting();
app.MapControllers();
app.MapEndpoints();

app.Run();

using Microsoft.EntityFrameworkCore;
using MinimalApi.Endpoint.Extensions;
using OrderService.api.Data;
using OrderService.api.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<OrderDbContext>(c =>
{
    var connectionString = builder.Configuration.GetConnectionString("OrderConnection");
    c.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());
});

builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

builder.Services.AddEndpoints();

builder.Services.AddSwaggerGen();

var app = builder.Build();

app.Logger.LogInformation("App created...");

app.Logger.LogInformation("Seeding Database...");

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

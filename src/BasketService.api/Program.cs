using Microsoft.EntityFrameworkCore;
using BasketService.api.Data;
using BasketService.api.Interfaces;
using MinimalApi.Endpoint.Extensions;
using BasketService.api.Data.Queries;
using BasketService.api.Logging;
using BasketService.api.Endpoints;

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

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
builder.Services.AddScoped<IBasketService, BasketService.api.Services.BasketService>();
builder.Services.AddScoped<IBasketQueryService, BasketQueryService>();


builder.Services.AddEndpoints();

var app = builder.Build();

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

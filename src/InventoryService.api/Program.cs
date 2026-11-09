using InventoryService.api.Consumers;
using InventoryService.api.Endpoints;
using InventoryService.api.Interfaces;
using InventoryService.api.Producers;
using InventoryService.api.Repositories;
using InventoryService.api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInventoryRepositories(builder.Configuration);
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<InventoryCheckoutHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var configuredKey = configuration["Authentication:JwtSigningKey"];
        SymmetricSecurityKey? signingKey = null;
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            var key = Convert.FromBase64String(configuredKey);
            if (key.Length < 32)
                throw new InvalidOperationException("Authentication:JwtSigningKey must contain at least 32 bytes.");
            signingKey = new SymmetricSecurityKey(key);
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(InventoryEndpoints.AdminPolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("Administrators"));

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

app.UseAuthentication();
app.UseAuthorization();
app.MapInventoryEndpoints();
app.Run();

public partial class Program { }

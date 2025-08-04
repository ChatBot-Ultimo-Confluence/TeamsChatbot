using ConfluenceChatBot.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Enable pgvector globally
NpgsqlConnection.GlobalTypeMapper.UseVector();

// Register services using extensions
builder.Services
    .AddHttpClients()
    .AddAppServices()
    .AddSemanticKernel(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Middleware
var app = builder.Build();

// Add your global exception middleware BEFORE routing
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();
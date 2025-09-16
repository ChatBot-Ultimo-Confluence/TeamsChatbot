using ConfluenceChatBot.BackgroundServices;
using ConfluenceChatBot.Extensions;
using ConfluenceChatBot.Models;
using ConfluenceChatBot.Services;
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
    .Bind(s => s.AddAppServices())
    .Bind(s => s.AddSemanticKernel(builder.Configuration));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<ConfluenceSyncService>();

// Middleware
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();
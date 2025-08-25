using Bot.Worker;
using Bot.Worker.Options;
using Bot.Worker.Services;
using Bot.Worker.Services.Interfaces;
using Microsoft.Extensions.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<StockBotWorker>();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Services.AddSerilog();

// Add configuration or service dependencies here.
ServicesConfiguration.RegisterServices(builder.Services, builder.Configuration);

var host = builder.Build();

try
{
    Log.Information("Bot.Worker starting up");
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Bot.Worker terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
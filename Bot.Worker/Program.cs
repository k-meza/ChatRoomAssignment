using Bot.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

// Add configuration or service dependencies here.
ServicesConfiguration.RegisterServices(builder.Services, builder.Configuration);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Services.AddSerilog();

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
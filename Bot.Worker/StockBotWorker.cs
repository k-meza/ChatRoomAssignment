using Bot.Worker.Options;
using Bot.Worker.Services.Interfaces;
using Common.Model;

namespace Bot.Worker;

public class StockBotWorker : BackgroundService
{
    private readonly IRabbitMqService _rabbitMqService;
    private readonly IStockApiService _stockApiService;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ILogger<StockBotWorker> _logger;

    public StockBotWorker(
        IRabbitMqService rabbitMqService,
        IStockApiService stockApiService,
        RabbitMqOptions rabbitMqOptions,
        ILogger<StockBotWorker> logger)
    {
        _rabbitMqService = rabbitMqService;
        _stockApiService = stockApiService;
        _rabbitMqOptions = rabbitMqOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stock Bot Worker started at: {Time}", DateTimeOffset.Now);

        try
        {
            await _rabbitMqService.StartConsumingAsync<StockCommand>(
                _rabbitMqOptions.CommandsQueue,
                HandleStockCommandAsync);

            // Keep the worker running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stock Bot Worker stopped due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stock Bot Worker encountered an error");
        }
    }

    private async Task HandleStockCommandAsync(StockCommand command)
    {
        _logger.LogInformation("Processing stock command for {StockCode} in room {RoomId}", 
            command.StockCode, command.RoomId);

        try
        {
            var quote = await _stockApiService.GetStockQuoteAsync(command.StockCode);
            
            var botMessage = new BotMessage
            {
                RoomId = command.RoomId,
                Message = quote,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            await _rabbitMqService.PublishAsync(
                _rabbitMqOptions.EventsExchange,
                "bot.message",
                botMessage);

            _logger.LogInformation("Published bot message for stock {StockCode}: {Quote}", 
                command.StockCode, quote);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stock command for {StockCode}", command.StockCode);
            
            // Send error message
            var errorMessage = new BotMessage
            {
                RoomId = command.RoomId,
                Message = $"Sorry, I couldn't fetch the quote for {command.StockCode.ToUpperInvariant()}. Please try again later.",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            try
            {
                await _rabbitMqService.PublishAsync(
                    _rabbitMqOptions.EventsExchange,
                    "bot.message",
                    errorMessage);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx, "Failed to publish error message for {StockCode}", command.StockCode);
            }
        }
    }

    public override void Dispose()
    {
        _rabbitMqService?.Dispose();
        base.Dispose();
    }
}
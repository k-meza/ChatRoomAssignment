using System.Text;
using System.Text.Json;
using Bot.Worker.Options;
using Bot.Worker.Services.Interfaces;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Bot.Worker.Services;

public class RabbitMqService : IRabbitMqService, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqService> _logger;
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    public RabbitMqService(RabbitMqOptions options, ILogger<RabbitMqService> logger)
    {
        _options = options;
        _logger = logger;

        var factory = new ConnectionFactory()
        {
            HostName = _options.HostName,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            Port = _options.Port
        };

        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

        // Ensure infrastructure
        // I'm aware this constitutes an anti-pattern. I made it this way to keep this assignment simple.
        // The solution would be to create a separate service for this.
        // I would create a Factory class that would create the service and return it.
        EnsureInfrastructureAsync().GetAwaiter().GetResult();
    }


    public async Task PublishAsync<T>(string exchange, string routingKey, T message) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            await _channel.BasicPublishAsync(exchange, routingKey, body);

            _logger.LogDebug("Published message to {Exchange}/{RoutingKey}: {Message}",
                exchange, routingKey, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to {Exchange}/{RoutingKey}",
                exchange, routingKey);
            throw;
        }
    }

    public async Task StartConsumingAsync<T>(string queue, Func<T, Task> messageHandler) where T : class
    {
        try
        {
            // Make sure infra exists also before consuming in case service restarted
            await EnsureInfrastructureAsync();

            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.ReceivedAsync += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);

                try
                {
                    _logger.LogDebug("Received message from {Queue}: {Message}", queue, json);

                    var message = JsonSerializer.Deserialize<T>(json);
                    if (message != null)
                    {
                        await messageHandler(message);
                        await _channel.BasicAckAsync(ea.DeliveryTag, false);
                        _logger.LogDebug("Message processed successfully from {Queue}", queue);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize message from {Queue}: {Json}", queue, json);
                        await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message from {Queue}: {Message}", queue, json);
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
                }
            };

            await _channel.BasicConsumeAsync(queue, false, consumer);
            _logger.LogInformation("Started consuming messages from queue: {Queue}", queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start consuming from queue: {Queue}", queue);
            throw;
        }
    }

    private async Task EnsureInfrastructureAsync()
    {
        try
        {
            // Exchanges
            await _channel.ExchangeDeclareAsync(_options.CommandsExchange, ExchangeType.Direct, durable: true);
            await _channel.ExchangeDeclareAsync(_options.EventsExchange, ExchangeType.Direct, durable: true);

            // Queues
            await _channel.QueueDeclareAsync(_options.CommandsQueue, durable: true, exclusive: false,
                autoDelete: false);
            await _channel.QueueDeclareAsync(_options.EventsQueue, durable: true, exclusive: false, autoDelete: false);

            // Bindings
            await _channel.QueueBindAsync(_options.CommandsQueue, _options.CommandsExchange, "stock.command");
            await _channel.QueueBindAsync(_options.EventsQueue, _options.EventsExchange, "bot.message");

            _logger.LogInformation("RabbitMQ infrastructure ensured (exchanges, queues, bindings).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure RabbitMQ infrastructure");
            throw;
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
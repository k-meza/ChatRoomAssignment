using System.Text;
using System.Text.Json;
using API.Domain.Messaging.Interfaces;
using API.Options;
using RabbitMQ.Client;

namespace API.Domain.Messaging;

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

        // Declare exchanges and queues
        DeclareInfrastructure().GetAwaiter().GetResult();
    }

    private async Task DeclareInfrastructure()
    {
        try
        {
            // Declare exchanges
            await _channel.ExchangeDeclareAsync(_options.CommandsExchange, ExchangeType.Direct, durable: true);
            await _channel.ExchangeDeclareAsync(_options.EventsExchange, ExchangeType.Direct, durable: true);

            // Declare queues
            await _channel.QueueDeclareAsync(_options.CommandsQueue, durable: true, exclusive: false,
                autoDelete: false);
            await _channel.QueueDeclareAsync(_options.EventsQueue, durable: true, exclusive: false, autoDelete: false);

            // Bind queues to exchanges
            await _channel.QueueBindAsync(_options.CommandsQueue, _options.CommandsExchange, "stock.command");
            await _channel.QueueBindAsync(_options.EventsQueue, _options.EventsExchange, "bot.message");

            _logger.LogInformation("RabbitMQ infrastructure declared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to declare RabbitMQ infrastructure");
            throw;
        }
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

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
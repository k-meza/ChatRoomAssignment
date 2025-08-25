using System.Text;
using System.Text.Json;
using API.Controllers.Rooms;
using API.Domain.Messaging.Interfaces;
using API.Domain.Options;
using API.Hubs;
using API.Repositories.AppDbContext;
using API.Repositories.AppDbContext.Entites;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace API.Domain.Messaging;

public class StockCommandConsumer : BackgroundService, IStockCommandConsumer
{
    private readonly IRabbitMqService _rabbitMq;
    private readonly RabbitMqOptions _options;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<StockCommandConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection? _connection;
    private IChannel? _channel;

    public StockCommandConsumer(
        IRabbitMqService rabbitMq,
        RabbitMqOptions options,
        IHubContext<ChatHub> hubContext,
        ILogger<StockCommandConsumer> logger,
        IServiceScopeFactory scopeFactory)
    {
        _rabbitMq = rabbitMq;
        _options = options;
        _hubContext = hubContext;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SetupConsumer();

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task SetupConsumer()
    {
        try
        {
            var factory = new ConnectionFactory()
            {
                HostName = _options.HostName,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                Port = _options.Port
            };

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();

            // Consume bot events, not commands
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnEventReceived;

            // Manual ack for reliability
            await _channel.BasicConsumeAsync(
                queue: _options.EventsQueue,
                autoAck: false,
                consumer: consumer);

            _logger.LogInformation("Stock events consumer started (queue: {Queue})", _options.EventsQueue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup stock events consumer");
        }
    }

    private async Task OnEventReceived(object sender, BasicDeliverEventArgs e)
    {
        try
        {
            var body = e.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var botMsg = JsonSerializer.Deserialize<BotMessage>(json);

            if (botMsg == null)
            {
                _logger.LogWarning("Received null/invalid bot message: {Json}", json);
                if (_channel is not null) await _channel.BasicNackAsync(e.DeliveryTag, false, false);
                return;
            }

            await SendBotMessageToRoom(botMsg.RoomId.ToString(), botMsg.Message);
            if (_channel is not null) await _channel.BasicAckAsync(e.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing bot message event");
            if (_channel is not null) await _channel.BasicNackAsync(e.DeliveryTag, false, true);
        }
    }

    private async Task SendBotMessageToRoom(string roomId, string content, string botUserName = "StockBot")
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var message = new Message
        {
            Content = content,
            UserName = botUserName,
            UserId = null,
            ChatRoomId = Guid.Parse(roomId),
            CreatedAtUtc = DateTime.UtcNow,
            IsBotMessage = true
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync();

        var messageDto = new ChatMessageDto
        {
            Id = message.Id,
            Content = message.Content,
            UserName = message.UserName,
            CreatedAtUtc = message.CreatedAtUtc,
            IsBotMessage = message.IsBotMessage
        };

        await _hubContext.Clients.Group(roomId).SendAsync("ReceiveMessage", messageDto);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
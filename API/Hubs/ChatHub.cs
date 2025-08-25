using API.Controllers.Rooms;
using API.Domain.Messaging;
using API.Domain.Messaging.Interfaces;
using API.Domain.Options;
using API.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using API.Repositories.AppDbContext.Entites;
using API.Repositories.AppDbContext;
using Microsoft.EntityFrameworkCore;

namespace API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IRabbitMqService _bus;
    private readonly RabbitMqOptions _rmqOptions;

    public ChatHub(AppDbContext db,
        UserManager<AppUser> userManager,
        IRabbitMqService bus,
        RabbitMqOptions rmqOptions,
        ILogger<ChatHub> logger)
    {
        _db = db;
        _userManager = userManager;
        _bus = bus;
        _rmqOptions = rmqOptions;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userName = Context.User?.Identity?.Name;
        var connectionId = Context.ConnectionId;
        var clientIp = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        
        _logger.LogInformation("User {UserName} connected to ChatHub (ConnectionId: {ConnectionId}, IP: {ClientIp})", 
            userName, connectionId, clientIp);
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userName = Context.User?.Identity?.Name;
        var connectionId = Context.ConnectionId;
        
        if (exception != null)
        {
            _logger.LogWarning(exception, "User {UserName} disconnected from ChatHub with exception (ConnectionId: {ConnectionId})", 
                userName, connectionId);
        }
        else
        {
            _logger.LogInformation("User {UserName} disconnected from ChatHub (ConnectionId: {ConnectionId})", 
                userName, connectionId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinRoom(string roomId)
    {
        var userName = Context.User?.Identity?.Name;
        var connectionId = Context.ConnectionId;
        
        _logger.LogInformation("User {UserName} joining room {RoomId} (ConnectionId: {ConnectionId})", 
            userName, roomId, connectionId);
        
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

            // Load and send last 50 messages to the joining user
            var messages = await _db.Messages
                .Where(m => m.ChatRoomId == Guid.Parse(roomId))
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(50)
                .OrderBy(m => m.CreatedAtUtc)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    Content = m.Content,
                    UserName = m.UserName,
                    CreatedAtUtc = m.CreatedAtUtc,
                    IsBotMessage = m.IsBotMessage
                })
                .ToListAsync();

            await Clients.Caller.SendAsync("LoadMessages", messages);
            
            _logger.LogInformation("User {UserName} successfully joined room {RoomId} and loaded {MessageCount} messages (ConnectionId: {ConnectionId})", 
                userName, roomId, messages.Count, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join user {UserName} to room {RoomId} (ConnectionId: {ConnectionId})", 
                userName, roomId, connectionId);
            throw;
        }
    }

    public async Task LeaveRoom(string roomId)
    {
        var userName = Context.User?.Identity?.Name;
        var connectionId = Context.ConnectionId;
        
        _logger.LogInformation("User {UserName} leaving room {RoomId} (ConnectionId: {ConnectionId})", 
            userName, roomId, connectionId);
        
        try
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
            
            _logger.LogInformation("User {UserName} successfully left room {RoomId} (ConnectionId: {ConnectionId})", 
                userName, roomId, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove user {UserName} from room {RoomId} (ConnectionId: {ConnectionId})", 
                userName, roomId, connectionId);
            throw;
        }
    }

    public async Task SendMessage(string roomId, string content)
    {
        var userName = Context.User?.Identity?.Name;
        var connectionId = Context.ConnectionId;
        
        _logger.LogDebug("User {UserName} sending message to room {RoomId}: '{Content}' (ConnectionId: {ConnectionId})", 
            userName, roomId, content?.Length > 100 ? content[..100] + "..." : content, connectionId);
        
        var user = await _userManager.GetUserAsync(Context.User);
        if (user == null)
        {
            _logger.LogWarning("Failed to get user context for message in room {RoomId} (ConnectionId: {ConnectionId})", 
                roomId, connectionId);
            return;
        }

        try
        {
            // Parse /stock command robustly (handles /stock, /stock=CODE, /stock CODE, ^spx, etc.)
            var stockParse = CommandParser.ParseStock(content);
            if (stockParse.IsStockCommand)
            {
                _logger.LogInformation("User {UserName} issued stock command in room {RoomId}: '{StockCode}' (ConnectionId: {ConnectionId})", 
                    userName, roomId, stockParse.HasCode ? stockParse.Code : "INVALID", connectionId);
                
                if (!stockParse.HasCode)
                {
                    _logger.LogWarning("Invalid stock command from user {UserName} in room {RoomId}: {Error} (ConnectionId: {ConnectionId})", 
                        userName, roomId, stockParse.Error, connectionId);
                    
                    // Inform the user about correct usage right in the chat
                    await SendBotMessage(roomId, stockParse.Error ?? "Invalid /stock command.");
                    return;
                }

                var stockCommand = new StockCommand
                {
                    StockCode = stockParse.Code,
                    RoomId = Guid.Parse(roomId),
                    RequestUserId = user.Id
                };

                await _bus.PublishAsync(_rmqOptions.CommandsExchange, "stock.command", stockCommand);
                
                _logger.LogInformation("Stock command published for user {UserName}: {StockCode} in room {RoomId} (ConnectionId: {ConnectionId})", 
                    userName, stockParse.Code, roomId, connectionId);
                return;
            }

            // Save regular message to database
            var message = new Message
            {
                Content = content,
                UserName = user.UserName!,
                UserId = user.Id,
                ChatRoomId = Guid.Parse(roomId),
                CreatedAtUtc = DateTime.UtcNow,
                IsBotMessage = false
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            // Send message to all clients in the room using DTO
            var messageDto = new ChatMessageDto
            {
                Id = message.Id,
                Content = message.Content,
                UserName = message.UserName,
                CreatedAtUtc = message.CreatedAtUtc,
                IsBotMessage = message.IsBotMessage
            };

            await Clients.Group(roomId).SendAsync("ReceiveMessage", messageDto);
            
            _logger.LogInformation("Message sent successfully by user {UserName} in room {RoomId} (MessageId: {MessageId}, ConnectionId: {ConnectionId})", 
                userName, roomId, message.Id, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message from user {UserName} in room {RoomId} (ConnectionId: {ConnectionId})", 
                userName, roomId, connectionId);
            throw;
        }
    }

    public async Task SendBotMessage(string roomId, string content, string botUserName = "StockBot")
    {
        _logger.LogInformation("Sending bot message from {BotUserName} to room {RoomId}: '{Content}'", 
            botUserName, roomId, content?.Length > 100 ? content[..100] + "..." : content);
        
        try
        {
            var message = new Message
            {
                Content = content,
                UserName = botUserName,
                UserId = null,
                ChatRoomId = Guid.Parse(roomId),
                CreatedAtUtc = DateTime.UtcNow,
                IsBotMessage = true
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            // Send message to all clients in the room using DTO
            var messageDto = new ChatMessageDto
            {
                Id = message.Id,
                Content = message.Content,
                UserName = message.UserName,
                CreatedAtUtc = message.CreatedAtUtc,
                IsBotMessage = message.IsBotMessage
            };

            await Clients.Group(roomId).SendAsync("ReceiveMessage", messageDto);
            
            _logger.LogInformation("Bot message sent successfully from {BotUserName} to room {RoomId} (MessageId: {MessageId})", 
                botUserName, roomId, message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send bot message from {BotUserName} to room {RoomId}", 
                botUserName, roomId);
            throw;
        }
    }
}
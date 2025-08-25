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

    public async Task JoinRoom(string roomId)
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
    }

    public async Task LeaveRoom(string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
    }

    public async Task SendMessage(string roomId, string content)
    {
        var user = await _userManager.GetUserAsync(Context.User);
        if (user == null) return;

        // Parse /stock command robustly (handles /stock, /stock=CODE, /stock CODE, ^spx, etc.)
        var stockParse = CommandParser.ParseStock(content);
        if (stockParse.IsStockCommand)
        {
            if (!stockParse.HasCode)
            {
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
    }

    public async Task SendBotMessage(string roomId, string content, string botUserName = "StockBot")
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
    }
}
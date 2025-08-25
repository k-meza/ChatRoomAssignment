using API.Domain.Messaging.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using API.Repositories.AppDbContext.Entites;
using API.Repositories.AppDbContext;
using Microsoft.EntityFrameworkCore;
using Common.Model;
using API.Options;
using Common.Helpers;

namespace API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IRabbitMqService _bus;
    private readonly RabbitMqOptions _rmqOptions;

    public ChatHub(AppDbContext db,
        UserManager<AppUser> userManager,
        IRabbitMqService bus,
        RabbitMqOptions rmqOptions)
    {
        _db = db;
        _userManager = userManager;
        _bus = bus;
        _rmqOptions = rmqOptions;
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
            .Select(m => new
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

        if (CommandParser.TryParseStock(content, out var stockCode))
        {
            var stockCommand = new StockCommand
            {
                StockCode = stockCode,
                RoomId = Guid.Parse(roomId),
                RequestUserId = user.Id
            };

            // Publish the command to RabbitMQ (do not persist the command as a chat message)
            await _bus.PublishAsync(_rmqOptions.CommandsExchange, "stock.command", stockCommand);

                // Acknowledge command received (optional)
                await Clients.Caller.SendAsync("CommandAcknowledged", $"Stock command for {stockCode} received");
                return; // Don't save stock commands to database
            }
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

        // Send message to all clients in the room
        await Clients.Group(roomId).SendAsync("ReceiveMessage", new
        {
            Id = message.Id,
            Content = message.Content,
            UserName = message.UserName,
            CreatedAtUtc = message.CreatedAtUtc,
            IsBotMessage = message.IsBotMessage
        });
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

        await Clients.Group(roomId).SendAsync("ReceiveMessage", new
        {
            Id = message.Id,
            Content = message.Content,
            UserName = message.UserName,
            CreatedAtUtc = message.CreatedAtUtc,
            IsBotMessage = message.IsBotMessage
        });
    }
}
using API.Domain.Messaging.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using API.Repositories.AppDbContext.Entites;
using API.Repositories.AppDbContext;
using Microsoft.EntityFrameworkCore;
using Common.Model;

namespace API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IServiceProvider _serviceProvider;

    public ChatHub(AppDbContext db, UserManager<AppUser> userManager, IServiceProvider serviceProvider)
    {
        _db = db;
        _userManager = userManager;
        _serviceProvider = serviceProvider;
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

        // Check if it's a stock command
        if (content.StartsWith("/stock=", StringComparison.OrdinalIgnoreCase))
        {
            var stockCode = content.Substring(7).Trim();
            if (!string.IsNullOrEmpty(stockCode))
            {
                // Send stock command to message broker (you'll implement this service)
                var stockCommand = new StockCommand
                {
                    StockCode = stockCode,
                    RoomId = Guid.Parse(roomId),
                    RequestUserId = user.Id
                };

                // Inject and use your message broker service here
                // For now, let's assume we have an IStockCommandService
                var stockService = _serviceProvider.GetService<IStockCommandService>();
                if (stockService != null)
                {
                    await stockService.ProcessStockCommandAsync(stockCommand);
                }

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
        // This method will be called by the bot service
        var message = new Message
        {
            Content = content,
            UserName = botUserName,
            UserId = null, // Bot messages don't have a user ID
            ChatRoomId = Guid.Parse(roomId),
            CreatedAtUtc = DateTime.UtcNow,
            IsBotMessage = true
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        // Send bot message to all clients in the room
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
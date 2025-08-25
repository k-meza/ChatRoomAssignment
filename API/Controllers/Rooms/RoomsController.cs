using API.Repositories.AppDbContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers.Rooms;

[ApiController]
[Route("api/rooms")]
public class RoomsController : ControllerBase
{
    private readonly ILogger<RoomsController> _logger;
    private readonly AppDbContext _db;

    public RoomsController(AppDbContext db, ILogger<RoomsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    public async Task<IEnumerable<RoomDto>> GetRoom()
    {
        var userName = User.Identity?.Name;
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        
        _logger.LogInformation("User {UserName} requesting rooms list (IP: {ClientIp})", userName, clientIp);
        
        try
        {
            var rooms = await _db.ChatRooms
                .OrderBy(r => r.Name)
                .Select(r => new RoomDto
                {
                    Id = r.Id,
                    Name = r.Name
                })
                .ToListAsync();

            _logger.LogInformation("Successfully retrieved {RoomCount} rooms for user {UserName} (IP: {ClientIp})", 
                rooms.Count, userName, clientIp);
            
            return rooms;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve rooms for user {UserName} (IP: {ClientIp})", userName, clientIp);
            throw;
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        var userName = User.Identity?.Name;
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var roomName = request.Name?.Trim();
        
        _logger.LogInformation("User {UserName} attempting to create room '{RoomName}' (IP: {ClientIp})", 
            userName, roomName, clientIp);
        
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            _logger.LogWarning("Room creation failed for user {UserName}: Empty room name (IP: {ClientIp})", 
                userName, clientIp);
            return BadRequest("Room name is required");
        }

        try
        {
            var existingRoom = await _db.ChatRooms
                .FirstOrDefaultAsync(r => r.Name == roomName);

            if (existingRoom != null)
            {
                _logger.LogWarning("Room creation failed for user {UserName}: Room '{RoomName}' already exists (ID: {ExistingRoomId}) (IP: {ClientIp})", 
                    userName, roomName, existingRoom.Id, clientIp);
                return Conflict("Room with this name already exists");
            }

            var room = new API.Repositories.AppDbContext.Entites.ChatRoom
            {
                Name = roomName
            };

            _db.ChatRooms.Add(room);
            await _db.SaveChangesAsync();

            _logger.LogInformation("User {UserName} successfully created room '{RoomName}' (ID: {RoomId}) (IP: {ClientIp})", 
                userName, roomName, room.Id, clientIp);

            // Count total rooms for monitoring
            var totalRooms = await _db.ChatRooms.CountAsync();
            _logger.LogInformation("Total rooms in system: {TotalRooms}", totalRooms);

            return Ok(new RoomDto
            {
                Id = room.Id,
                Name = room.Name
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create room '{RoomName}' for user {UserName} (IP: {ClientIp})", 
                roomName, userName, clientIp);
            throw;
        }
    }
}
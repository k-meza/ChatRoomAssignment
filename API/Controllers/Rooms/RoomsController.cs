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
        return await _db.ChatRooms
            .OrderBy(r => r.Name)
            .Select(r => new RoomDto
            {
                Id = r.Id,
                Name = r.Name
            })
            .ToListAsync();
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Room name is required");

        var existingRoom = await _db.ChatRooms
            .FirstOrDefaultAsync(r => r.Name == request.Name.Trim());

        if (existingRoom != null)
            return Conflict("Room with this name already exists");

        var room = new API.Repositories.AppDbContext.Entites.ChatRoom
        {
            Name = request.Name.Trim()
        };

        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync();

        return Ok(new RoomDto
        {
            Id = room.Id,
            Name = room.Name
        });
    }
}
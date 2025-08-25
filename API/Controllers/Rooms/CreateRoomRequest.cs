namespace API.Controllers.Rooms;

public record CreateRoomRequest()
{
    public string Name { get; set; }
}
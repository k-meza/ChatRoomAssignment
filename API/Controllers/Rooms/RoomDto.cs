namespace API.Controllers.Rooms;

public record RoomDto()
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
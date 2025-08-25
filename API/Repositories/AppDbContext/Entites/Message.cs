namespace API.Repositories.AppDbContext.Entites;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string UserName { get; set; } = null!;
    public bool IsBotMessage { get; set; } = false;

    public string? UserId { get; set; }
    public AppUser? User { get; set; }

    public Guid ChatRoomId { get; set; }
    public ChatRoom ChatRoom { get; set; } = null!;
}
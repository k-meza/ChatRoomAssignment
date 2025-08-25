namespace API.Controllers.Rooms;

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public bool IsBotMessage { get; set; }
}
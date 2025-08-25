namespace Bot.Worker.Model;

public record BotMessage
{
    public Guid RoomId { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
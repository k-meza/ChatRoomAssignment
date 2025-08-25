namespace API.Domain.Messaging;

public record BotMessage
{
    public Guid RoomId { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
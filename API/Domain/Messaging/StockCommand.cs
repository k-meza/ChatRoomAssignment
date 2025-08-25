namespace API.Domain.Messaging;

public record StockCommand
{
    public string StockCode { get; init; } = string.Empty;
    public Guid RoomId { get; init; }
    public string RequestUserId { get; init; } = string.Empty;
}
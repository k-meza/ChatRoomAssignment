using Common.Model;

namespace API.Domain.Messaging.Interfaces;

public interface IStockCommandService
{
    Task ProcessStockCommandAsync(StockCommand command);
}

namespace API.Domain.Messaging.Interfaces;

public interface IStockCommandConsumer
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

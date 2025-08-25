namespace API.Domain.Messaging.Interfaces;

public interface IRabbitMqService
{
    Task PublishAsync<T>(string exchange, string routingKey, T message) where T : class;
    void Dispose();
}

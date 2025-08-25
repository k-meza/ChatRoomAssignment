namespace Bot.Worker.Services.Interfaces;

public interface IRabbitMqService
{
    Task PublishAsync<T>(string exchange, string routingKey, T message) where T : class;
    Task StartConsumingAsync<T>(string queue, Func<T, Task> messageHandler) where T : class;
    void Dispose();

}
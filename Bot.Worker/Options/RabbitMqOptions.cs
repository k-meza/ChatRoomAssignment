namespace Bot.Worker.Options;

public class RabbitMqOptions
{
    public string HostName { get; set; }
    public string UserName { get; set; }
    public string Password { get; set; }
    public string VirtualHost { get; set; }
    public string CommandsQueue { get; set; }
    public string EventsQueue { get; set; }
    public int Port { get; set; }
    public string CommandsExchange { get; set; }
    public string EventsExchange { get; set; }
}
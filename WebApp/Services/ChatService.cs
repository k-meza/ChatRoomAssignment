using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.AspNetCore.Http.Connections;

namespace WebApp.Services;

public class ChatService
{
    private readonly HttpClient _http;
    private readonly NavigationManager _nav;

    public ChatService(HttpClient http, NavigationManager nav)
    {
        _http = http;
        _nav = nav;
    }

    public async Task<List<ChatMessageDto>> GetRecentMessagesAsync(Guid roomId)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"api/rooms/{roomId}/messages?limit=50");
            req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return new();
            var msgs = await res.Content.ReadFromJsonAsync<List<ChatMessageDto>>();
            return msgs ?? new();
        }
        catch
        {
            return new();
        }
    }

    public async Task<HubConnection> ConnectAsync(Guid roomId)
    {
        var baseUri = _http.BaseAddress?.ToString()?.TrimEnd('/') ?? "";
        var hubUrl = $"{baseUri}/chathub";

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                // WebSockets ensures cookies are sent with wss
                opts.SkipNegotiation = true;
                opts.Transports = HttpTransportType.WebSockets;
            })
            .WithAutomaticReconnect(new[]
                { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        // Rejoin group after reconnect
        connection.Reconnected += async _ =>
        {
            try
            {
                await connection.InvokeAsync("JoinRoom", roomId.ToString());
            }
            catch
            {
                // swallow; next reconnect attempt will retry
            }
        };

        // Optional: log for troubleshooting
        connection.Closed += ex =>
        {
            Console.WriteLine($"SignalR closed: {ex?.Message}");
            return Task.CompletedTask;
        };
        connection.Reconnecting += ex =>
        {
            Console.WriteLine($"SignalR reconnecting: {ex?.Message}");
            return Task.CompletedTask;
        };
        connection.Reconnected += connectionId =>
        {
            Console.WriteLine($"SignalR reconnected: {connectionId}");
            return Task.CompletedTask;
        };

        await connection.StartAsync();
        return connection;
    }

    public Task JoinRoomAsync(HubConnection hub, Guid roomId)
        => hub.InvokeAsync("JoinRoom", roomId.ToString());

    public Task LeaveRoomAsync(HubConnection hub, Guid roomId)
        => hub.InvokeAsync("LeaveRoom", roomId.ToString());

    public Task SendMessageAsync(HubConnection hub, Guid roomId, string content)
        => hub.InvokeAsync("SendMessage", roomId.ToString(), content);
}

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public bool IsBotMessage { get; set; }
}
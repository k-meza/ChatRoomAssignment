using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace WebApp.Services;

public class RoomsService
{
    private readonly HttpClient _http;

    public RoomsService(HttpClient http) => _http = http;

    public async Task<List<RoomDto>> GetRoomsAsync()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "api/rooms");
        req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return new();
        var data = await res.Content.ReadFromJsonAsync<List<RoomDto>>();
        return data ?? new();
    }

    public async Task<RoomDto?> CreateRoomAsync(CreateRoomRequest reqBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "api/rooms")
        {
            Content = JsonContent.Create(reqBody)
        };
        req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<RoomDto>();
    }
}

public record RoomDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public record CreateRoomRequest
{
    public string Name { get; set; } = "";
}
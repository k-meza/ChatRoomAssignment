using System.Net.Http.Json;
using System.Net.Http;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace WebApp.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly AppState _state;

    public AuthService(HttpClient http, AppState state)
    {
        _http = http;
        _state = state;
    }

    public async Task<(bool ok, string? user, string? error)> RegisterAsync(RegisterRequest req)
    {
        var httpReq = new HttpRequestMessage(HttpMethod.Post, "api/auth/register")
        {
            Content = JsonContent.Create(req)
        };
        httpReq.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var res = await _http.SendAsync(httpReq);
        if (!res.IsSuccessStatusCode)
            return (false, null, await ReadError(res));
        var json = await res.Content.ReadFromJsonAsync<UserResponse>();
        _state.SetUser(json!.user);
        return (true, json!.user, null);
    }

    public async Task<(bool ok, string? user, string? error)> LoginAsync(LoginRequest req)
    {
        var httpReq = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(req)
        };
        httpReq.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var res = await _http.SendAsync(httpReq);
        if (!res.IsSuccessStatusCode)
            return (false, null, await ReadError(res));
        var json = await res.Content.ReadFromJsonAsync<UserResponse>();
        _state.SetUser(json!.user);
        return (true, json!.user, null);
    }

    public async Task LogoutAsync()
    {
        var httpReq = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
        httpReq.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        await _http.SendAsync(httpReq);
    }

    public async Task<bool> MeAsync()
    {
        var httpReq = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        httpReq.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var res = await _http.SendAsync(httpReq);
        if (!res.IsSuccessStatusCode) return false;
        var json = await res.Content.ReadFromJsonAsync<UserResponse>();
        if (json?.user is not null) _state.SetUser(json.user);
        return json?.user is not null;
    }

    private static async Task<string> ReadError(HttpResponseMessage res)
        => (await res.Content.ReadAsStringAsync()) ?? "Error";

    private record UserResponse(string user);
}

public record RegisterRequest
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}

public record LoginRequest
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}
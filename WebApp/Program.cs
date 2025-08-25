using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebApp;
using WebApp.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:5001";

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(apiBase)
});

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RoomsService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<AppState>();

await builder.Build().RunAsync();
namespace WebApp.Services;

public class AppState
{
    public string? UserName { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserName);

    public void SetUser(string user) => UserName = user;
    public void ClearUser() => UserName = null;
}
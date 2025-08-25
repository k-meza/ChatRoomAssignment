using API.Repositories.AppDbContext.Entites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;

namespace API.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private static readonly ConcurrentDictionary<string, DateTime> _activeSessions = new();

    public AuthController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        _logger.LogInformation("Registration attempt for username: {UserName}", req.UserName?.Trim());
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Registration failed for {UserName}: Invalid model state", req.UserName?.Trim());
            return ValidationProblem(ModelState);
        }

        var userName = req.UserName.Trim();
        var user = new AppUser { UserName = userName, Email = $"{userName}@local" };
        var res = await _userManager.CreateAsync(user, req.Password);
        if (!res.Succeeded)
        {
            _logger.LogWarning("Registration failed for {UserName}: {Errors}", userName, 
                string.Join(", ", res.Errors.Select(e => e.Description)));
            return BadRequest(res.Errors.Select(e => e.Description));
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        _logger.LogInformation("User {UserName} registered and signed in successfully", userName);
        return Ok(new { user = user.UserName });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody, Required] LoginRequest req)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        _logger.LogInformation("Login attempt for username: {UserName} from IP: {ClientIp}", 
            req.UserName?.Trim(), clientIp);
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Login failed for {UserName}: Invalid model state", req.UserName?.Trim());
            return ValidationProblem(ModelState);
        }

        var userName = req.UserName.Trim();
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            _logger.LogWarning("Login failed for {UserName}: User not found (IP: {ClientIp})", userName, clientIp);
            return Unauthorized();
        }

        // Check if user is already logged in
        if (_activeSessions.ContainsKey(userName))
        {
            _logger.LogWarning("Login blocked for {UserName}: User already has an active session (IP: {ClientIp})", 
                userName, clientIp);
            return BadRequest("User is already logged in from another session");
        }

        var check = await _signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: false);
        if (!check.Succeeded)
        {
            _logger.LogWarning("Login failed for {UserName}: Invalid password (IP: {ClientIp})", userName, clientIp);
            return Unauthorized();
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        
        // Track active session
        _activeSessions[userName] = DateTime.UtcNow;
        
        _logger.LogInformation("User {UserName} logged in successfully (IP: {ClientIp}). Active sessions: {ActiveSessionCount}", 
            userName, clientIp, _activeSessions.Count);
        
        return Ok(new { user = user.UserName });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userName = User.Identity?.Name;
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        
        _logger.LogInformation("Logout request for user: {UserName} (IP: {ClientIp})", userName, clientIp);
        
        await _signInManager.SignOutAsync();
        
        // Remove from active sessions
        if (!string.IsNullOrEmpty(userName))
        {
            var removed = _activeSessions.TryRemove(userName, out var sessionTime);
            if (removed)
            {
                var sessionDuration = DateTime.UtcNow - sessionTime;
                _logger.LogInformation("User {UserName} logged out successfully. Session duration: {Duration} (IP: {ClientIp}). Active sessions: {ActiveSessionCount}", 
                    userName, sessionDuration.ToString(@"hh\:mm\:ss"), clientIp, _activeSessions.Count);
            }
            else
            {
                _logger.LogWarning("User {UserName} logged out but session was not found in active sessions (IP: {ClientIp})", 
                    userName, clientIp);
            }
        }
        else
        {
            _logger.LogWarning("Logout request with no user identity (IP: {ClientIp})", clientIp);
        }
        
        return Ok();
    }

    // For debugging auth state from the client
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userName = User.Identity?.Name;
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        
        if (User.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug("Auth check for {UserName}: Authenticated (IP: {ClientIp})", userName, clientIp);
            return Ok(new { user = userName });
        }
        
        _logger.LogDebug("Auth check: Not authenticated (IP: {ClientIp})", clientIp);
        return Unauthorized();
    }
}
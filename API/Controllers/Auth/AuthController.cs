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
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userName = req.UserName.Trim();
        var user = new AppUser { UserName = userName, Email = $"{userName}@local" };
        var res = await _userManager.CreateAsync(user, req.Password);
        if (!res.Succeeded)
            return BadRequest(res.Errors.Select(e => e.Description));

        await _signInManager.SignInAsync(user, isPersistent: false);
        return Ok(new { user = user.UserName });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody, Required] LoginRequest req)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userName = req.UserName.Trim();
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null) return Unauthorized();

        // Check if user is already logged in
        if (_activeSessions.ContainsKey(userName))
        {
            return BadRequest("User is already logged in from another session");
        }

        var check = await _signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: false);
        if (!check.Succeeded) return Unauthorized();

        await _signInManager.SignInAsync(user, isPersistent: false);
        
        // Track active session
        _activeSessions[userName] = DateTime.UtcNow;
        
        return Ok(new { user = user.UserName });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userName = User.Identity?.Name;
        
        await _signInManager.SignOutAsync();
        
        // Remove from active sessions
        if (!string.IsNullOrEmpty(userName))
        {
            _activeSessions.TryRemove(userName, out _);
        }
        
        return Ok();
    }

    // For debugging auth state from the client
    [HttpGet("me")]
    public IActionResult Me()
        => User.Identity?.IsAuthenticated == true
            ? Ok(new { user = User.Identity!.Name })
            : Unauthorized();
}
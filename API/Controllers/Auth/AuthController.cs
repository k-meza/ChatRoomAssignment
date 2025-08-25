using API.Repositories.AppDbContext.Entites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;

    public AuthController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
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

        var user = await _userManager.FindByNameAsync(req.UserName.Trim());
        if (user is null) return Unauthorized();

        var check = await _signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: false);
        if (!check.Succeeded) return Unauthorized();

        await _signInManager.SignInAsync(user, isPersistent: false);
        return Ok(new { user = user.UserName });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok();
    }

    // Handy for debugging auth state from the client
    [HttpGet("me")]
    public IActionResult Me()
        => User.Identity?.IsAuthenticated == true
            ? Ok(new { user = User.Identity!.Name })
            : Unauthorized();
}
namespace API.Controllers.Auth;

using System.ComponentModel.DataAnnotations;

public record RegisterRequest()
{
    [Required]
    public string UserName { get; set; }

    [Required]
    public string Password { get; set; }
}
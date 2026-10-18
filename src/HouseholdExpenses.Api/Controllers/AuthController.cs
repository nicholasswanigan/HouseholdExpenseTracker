using HouseholdExpenses.Api.Auth;
using HouseholdExpenses.Api.Data;
using HouseholdExpenses.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdExpenses.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IJwtTokenService tokens,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Create an account and receive a JWT.</summary>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim()
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }

        logger.LogInformation("User {UserId} registered", user.Id);
        return Ok(BuildResponse(user));
    }

    /// <summary>Exchange email + password for a JWT.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return Unauthorized(new ProblemDetails { Status = 401, Title = "Invalid email or password" });

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            logger.LogWarning("Locked-out user {UserId} attempted login", user.Id);
            return Unauthorized(new ProblemDetails { Status = 401, Title = "Account temporarily locked. Try again later." });
        }
        if (!result.Succeeded)
        {
            logger.LogWarning("Failed login for user {UserId}", user.Id);
            return Unauthorized(new ProblemDetails { Status = 401, Title = "Invalid email or password" });
        }

        return Ok(BuildResponse(user));
    }

    private AuthResponse BuildResponse(ApplicationUser user)
    {
        var (token, expires) = tokens.Create(user);
        return new AuthResponse(token, expires, new UserDto(user.Id, user.Email ?? "", user.DisplayName));
    }
}

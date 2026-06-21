using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NOAH.Contracts.Auth;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

/// <summary>
/// Handles temporary login and identity inspection for NOAH clients.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(
    IAuthenticationService authenticationService,
    ILogger<AuthController> logger)
    : ControllerBase
{
    /// <summary>
    /// Exchanges a configured username and password for a temporary bearer token.
    /// </summary>
    /// <param name="request">The credentials to validate.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The issued bearer token.</returns>
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> LoginAsync(
        [FromBody] LoginRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Request"] = ["Request body is required."]
            }));
        }

        Dictionary<string, string[]> validationErrors = new();

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            validationErrors[nameof(request.Username)] = ["Username is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            validationErrors[nameof(request.Password)] = ["Password is required."];
        }

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors));
        }

        try
        {
            LoginResponse response = await authenticationService.LoginAsync(request, cancellationToken);
            logger.LogInformation("Issued temporary access token for {Username}.", response.User.Username);
            return Ok(response);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Failed login attempt for {Username}.", request.Username);
            return Unauthorized(new ProblemDetails
            {
                Title = "Authentication failed.",
                Detail = exception.Message,
                Status = StatusCodes.Status401Unauthorized
            });
        }
    }

    /// <summary>
    /// Returns the authenticated NOAH user for the current access token or API key.
    /// </summary>
    /// <returns>The current authenticated user.</returns>
    [HttpGet("me")]
    public ActionResult<AuthenticatedUserDto> GetCurrentUser()
    {
        return Ok(authenticationService.GetCurrentUser(User));
    }
}

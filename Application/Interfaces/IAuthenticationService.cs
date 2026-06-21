using System.Security.Claims;
using NOAH.Contracts.Auth;

namespace Application.Interfaces;

/// <summary>
/// Issues temporary access tokens and maps authenticated user information.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Validates a login request and issues a NOAH bearer token.
    /// </summary>
    /// <param name="request">The credentials to validate.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The issued access token and user metadata.</returns>
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps the current principal into the NOAH user contract.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>The mapped NOAH user.</returns>
    AuthenticatedUserDto GetCurrentUser(ClaimsPrincipal principal);
}

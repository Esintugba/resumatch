using Api.Application.Auth;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Entities.Enums;
using Api.DTOs.Auth;
using Api.Infrastructure.Authentication;
using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IAuthSessionService _sessionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly RefreshTokenCookie _refreshTokenCookie;

    public AuthController(
        IAuthService authService,
        IAuthSessionService sessionService,
        ICurrentUserService currentUserService,
        RefreshTokenCookie refreshTokenCookie)
    {
        _authService = authService;
        _sessionService = sessionService;
        _currentUserService = currentUserService;
        _refreshTokenCookie = refreshTokenCookie;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [EnableRateLimiting(AuthenticationRateLimitPolicyNames.Register)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(
            request,
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, SignIn(result));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(AuthenticationRateLimitPolicyNames.Login)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(
            request,
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return Ok(SignIn(result));
    }

    /// <summary>
    /// Rotates the refresh token cookie and returns a new access token. Needs
    /// no access token, so it also works after the access token expired.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [RequireCsrfProtection]
    [EnableRateLimiting(AuthenticationRateLimitPolicyNames.Refresh)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        var outcome = await _authService.RefreshAsync(
            _refreshTokenCookie.Read(Request),
            cancellationToken);

        switch (outcome.Status)
        {
            case RefreshStatus.Succeeded:
                return Ok(SignIn(outcome.Result!));

            // The faster tab already rotated this token and the browser holds
            // its new cookie. Deleting the cookie here would sign that tab out.
            case RefreshStatus.Superseded:
                Response.Headers.CacheControl = "no-store";
                return AuthProblem(
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    "The refresh token was just rotated by another request. Retry once.",
                    ApiErrorCodes.RefreshTokenSuperseded);

            default:
                _refreshTokenCookie.Delete(Response);
                return AuthProblem(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "No valid session was found. Sign in again.",
                    ApiErrorCodes.Unauthorized);
        }
    }

    /// <summary>
    /// Revokes the session of the refresh token cookie and removes the cookie.
    /// Always 204, so it reveals nothing about the cookie it received.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    [RequireCsrfProtection]
    [EnableRateLimiting(AuthenticationRateLimitPolicyNames.Logout)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(
            _refreshTokenCookie.Read(Request),
            cancellationToken);
        _refreshTokenCookie.Delete(Response);
        return NoContent();
    }

    [Authorize]
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<AuthSessionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuthSessionResponse>>> GetSessions(
        CancellationToken cancellationToken)
    {
        var response = await _sessionService.ListAsync(
            _currentUserService.GetRequiredUserId(),
            _currentUserService.SessionId,
            cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var revoked = await _sessionService.RevokeAsync(
            _currentUserService.GetRequiredUserId(),
            sessionId,
            AuthSessionRevocationReason.SignedOut,
            cancellationToken);
        if (!revoked)
        {
            throw new NotFoundException("Session was not found.");
        }

        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        await _sessionService.RevokeAllAsync(
            _currentUserService.GetRequiredUserId(),
            AuthSessionRevocationReason.SignedOutEverywhere,
            cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserResponse>> GetCurrentUser(
        CancellationToken cancellationToken)
    {
        var response = await _authService.GetCurrentUserAsync(cancellationToken);
        return Ok(response);
    }

    private AuthResponse SignIn(AuthenticationResult result)
    {
        _refreshTokenCookie.Write(Response, result.RefreshToken, result.RefreshTokenExpiresAt);
        return result.Response;
    }

    // Written here rather than thrown, because the exception handler clears
    // response headers and would drop the cookie deletion.
    private ObjectResult AuthProblem(int status, string title, string detail, string code)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext,
            status,
            title,
            detail: detail,
            instance: Request.Path);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}

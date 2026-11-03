using Api.Application.Auth;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Entities.Enums;
using Api.DTOs.Auth;
using Api.Infrastructure.Authentication;
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

    public AuthController(
        IAuthService authService,
        IAuthSessionService sessionService,
        ICurrentUserService currentUserService)
    {
        _authService = authService;
        _sessionService = sessionService;
        _currentUserService = currentUserService;
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
        var response = await _authService.RegisterAsync(
            request,
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
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
        var response = await _authService.LoginAsync(
            request,
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return Ok(response);
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
}

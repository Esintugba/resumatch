using System.Security.Claims;
using Api.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Api.Infrastructure.Authentication;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            if (!IsAuthenticated)
            {
                return null;
            }

            var userIdClaim = _httpContextAccessor.HttpContext?.User
                .FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(userIdClaim, out var userId) && userId != Guid.Empty
                ? userId
                : null;
        }
    }

    public string? Email => IsAuthenticated
        ? _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        : null;

    public Guid GetRequiredUserId()
    {
        if (!IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        return UserId ?? throw new UnauthorizedAccessException(
            "The authenticated user identifier is missing or invalid.");
    }
}

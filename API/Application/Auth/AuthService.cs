using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Common.Models;
using Api.Data;
using Api.DTOs.Auth;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Auth;

public sealed class AuthService : IAuthService
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuthSessionService _sessionService;

    public AuthService(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ICurrentUserService currentUserService,
        IAuthSessionService sessionService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _currentUserService = currentUserService;
        _sessionService = sessionService;
    }

    public async Task<AuthenticationResult> RegisterAsync(
        RegisterRequest request,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);

        var emailExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                user => EF.Property<string>(user, "NormalizedEmail") == normalizedEmail,
                cancellationToken);

        if (emailExists)
        {
            throw new ConflictException(
                "Registration could not be completed with the supplied information.",
                ApiErrorCodes.RegistrationRejected);
        }

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            IsActive = true,
            IsEmailVerified = false
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await StartSessionAsync(user, userAgent, cancellationToken);
    }

    public async Task<AuthenticationResult> LoginAsync(
        LoginRequest request,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = NormalizeEmail(request.Email);

        var user = await _dbContext.Users.SingleOrDefaultAsync(
            user => EF.Property<string>(user, "NormalizedEmail") == normalizedEmail,
            cancellationToken);

        if (user is null)
        {
            _passwordHasher.PerformDummyVerification(request.Password);
            throw new UnauthorizedAccessException(InvalidCredentialsMessage);
        }

        var verification = _passwordHasher.Verify(request.Password, user.PasswordHash);

        if (!verification.Succeeded || !user.IsActive)
        {
            throw new UnauthorizedAccessException(InvalidCredentialsMessage);
        }

        if (verification.NeedsRehash)
        {
            user.PasswordHash = _passwordHasher.Hash(request.Password);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await StartSessionAsync(user, userAgent, cancellationToken);
    }

    public async Task<RefreshOutcome> RefreshAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        var result = await _sessionService.RefreshAsync(
            refreshToken ?? string.Empty,
            cancellationToken);
        if (result.Status != RefreshStatus.Succeeded)
        {
            return new RefreshOutcome(result.Status);
        }

        // The user was read in the rotation transaction, so the new access
        // token carries the current security stamp.
        var session = result.Session!;
        var accessToken = _jwtTokenService.GenerateAccessToken(session.User, session.Id);
        return new RefreshOutcome(
            RefreshStatus.Succeeded,
            new AuthenticationResult(
                CreateAuthResponse(session.User, accessToken),
                result.RefreshToken!,
                session.IdleExpiresAt));
    }

    public async Task LogoutAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        await _sessionService.RevokeByRefreshTokenAsync(
            refreshToken,
            AuthSessionRevocationReason.SignedOut,
            cancellationToken);
    }

    private async Task<AuthenticationResult> StartSessionAsync(
        User user,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var session = await _sessionService.StartAsync(
            user,
            DeviceLabel.FromUserAgent(userAgent),
            cancellationToken);
        var refreshToken = await _sessionService.IssueRefreshTokenAsync(
            session.Id,
            cancellationToken);
        var accessToken = _jwtTokenService.GenerateAccessToken(user, session.Id);
        return new AuthenticationResult(
            CreateAuthResponse(user, accessToken),
            refreshToken,
            session.IdleExpiresAt);
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();

        var user = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.Id == userId && user.IsActive,
                cancellationToken);

        if (user is null)
        {
            throw new UnauthorizedAccessException("The authenticated user is unavailable.");
        }

        return CreateCurrentUserResponse(user);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static AuthResponse CreateAuthResponse(
        User user,
        AccessTokenResult accessToken)
    {
        return new AuthResponse
        {
            AccessToken = accessToken.Token,
            ExpiresAt = accessToken.ExpiresAt,
            User = CreateCurrentUserResponse(user)
        };
    }

    private static CurrentUserResponse CreateCurrentUserResponse(User user)
    {
        return new CurrentUserResponse
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            ProfileImageUrl = user.ProfileImageUrl,
            IsEmailVerified = user.IsEmailVerified
        };
    }
}

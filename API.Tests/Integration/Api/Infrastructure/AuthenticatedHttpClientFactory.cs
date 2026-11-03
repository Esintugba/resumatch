using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using Api.Application.Common.Interfaces;
using Api.Data;
using Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace API.Tests.Integration.Api.Infrastructure;

public sealed class AuthenticatedHttpClientFactory
{
    private readonly ResuMatchWebApplicationFactory _application;

    public AuthenticatedHttpClientFactory(
        ResuMatchWebApplicationFactory application)
    {
        _application = application ??
            throw new ArgumentNullException(nameof(application));
    }

    public async Task<HttpClient> CreateAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty test user identifier is required.",
                nameof(user));
        }

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateForExistingUser(user);
    }

    public HttpClient CreateForExistingUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty test user identifier is required.",
                nameof(user));
        }

        return CreateWithBearerToken(CreateAccessToken(user));
    }

    /// <summary>
    /// Opens a session row for an existing user and returns a token for it.
    /// Written directly, so it also works for inactive users.
    /// </summary>
    public string CreateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        using var scope = _application.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = dbContext.Users.AsNoTracking().Single(item => item.Id == user.Id);
        // The application clock, which some tests move, decides session validity.
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = now,
            LastUsedAt = now,
            IdleExpiresAt = now.AddDays(365),
            AbsoluteExpiresAt = now.AddDays(365)
        };
        dbContext.AuthSessions.Add(session);
        dbContext.SaveChanges();

        return scope.ServiceProvider
            .GetRequiredService<IJwtTokenService>()
            .GenerateAccessToken(stored, session.Id)
            .Token;
    }

    public HttpClient CreateWithBearerToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException(
                "A bearer token is required.",
                nameof(token));
        }

        var client = _application.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static string CreateControlledToken(
        Guid userId,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt,
        string signingSecret =
            ResuMatchWebApplicationFactory.TestSigningSecret)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty test user identifier is required.",
                nameof(userId));
        }

        if (expiresAt <= notBefore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Token expiration must be after its not-before value.");
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(signingSecret);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The test signing secret must be valid Base64.",
                nameof(signingSecret),
                exception);
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D"))
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: ResuMatchWebApplicationFactory.TestIssuer,
            audience: ResuMatchWebApplicationFactory.TestAudience,
            claims: claims,
            notBefore: notBefore.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

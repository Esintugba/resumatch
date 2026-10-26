using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Api.Application.Common.Interfaces;
using Api.Application.Common.Models;
using Api.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Infrastructure.Authentication;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        ValidateOptions(_options);
        _signingKey = new SymmetricSecurityKey(
            JwtSigningKeyMaterial.Decode(_options.SecretKey));
    }

    public AccessTokenResult GenerateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty user identifier is required.", nameof(user));
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenExpirationMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D"))
        };

        var signingCredentials = new SigningCredentials(
            _signingKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: signingCredentials);

        var serializedToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new AccessTokenResult(serializedToken, expiresAt);
    }

    private static void ValidateOptions(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException("JWT issuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("JWT audience must be configured.");
        }

        if (!JwtSigningKeyMaterial.IsValid(options.SecretKey))
        {
            throw new InvalidOperationException(
                "JWT secret key must be valid Base64 that decodes to at least 32 bytes. " +
                "Generate it with a cryptographically secure random source.");
        }

        if (options.AccessTokenExpirationMinutes is <= 0 or
            > JwtOptions.MaximumAccessTokenExpirationMinutes)
        {
            throw new InvalidOperationException(
                $"JWT access token expiration must be between 1 and " +
                $"{JwtOptions.MaximumAccessTokenExpirationMinutes} minutes.");
        }
    }
}

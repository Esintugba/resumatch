using Api.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Api.Infrastructure.Authentication;

public sealed class PasswordHasher : IPasswordHasher
{
    // This is a non-secret, precomputed ASP.NET Core Identity hash used only
    // to keep the unknown-user login path computationally comparable.
    private const string DummyPasswordHash =
        "AQAAAAIAAYagAAAAEK5yepU8ZwVGkVdhlQsp4Cq9IxrWYfjQSyfTi5nZ/6PpHFj+g3J1rcptiGhbrsqi/g==";

    private static readonly object UserContext = new();

    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<object> _identityPasswordHasher = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        return _identityPasswordHasher.HashPassword(UserContext, password);
    }

    public PasswordVerificationOutcome Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return new PasswordVerificationOutcome(Succeeded: false, NeedsRehash: false);
        }

        PasswordVerificationResult result;

        try
        {
            result = _identityPasswordHasher.VerifyHashedPassword(
                UserContext,
                passwordHash,
                password);
        }
        catch (FormatException)
        {
            return new PasswordVerificationOutcome(Succeeded: false, NeedsRehash: false);
        }

        return result switch
        {
            PasswordVerificationResult.Success =>
                new PasswordVerificationOutcome(Succeeded: true, NeedsRehash: false),

            PasswordVerificationResult.SuccessRehashNeeded =>
                new PasswordVerificationOutcome(Succeeded: true, NeedsRehash: true),

            _ => new PasswordVerificationOutcome(Succeeded: false, NeedsRehash: false)
        };
    }

    public void PerformDummyVerification(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        _ = Verify(password, DummyPasswordHash);
    }
}

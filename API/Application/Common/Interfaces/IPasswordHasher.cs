namespace Api.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    PasswordVerificationOutcome Verify(string password, string passwordHash);
    void PerformDummyVerification(string password);
}

public readonly record struct PasswordVerificationOutcome(
    bool Succeeded,
    bool NeedsRehash);

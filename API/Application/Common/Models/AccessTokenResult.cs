namespace Api.Application.Common.Models;

public sealed record AccessTokenResult(
    string Token,
    DateTimeOffset ExpiresAt);

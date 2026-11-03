namespace Api.Entities.Enums;

public enum AuthSessionRevocationReason
{
    SignedOut = 0,
    SignedOutEverywhere = 1,
    RefreshTokenReused = 2,
    SessionLimitReached = 3,
    SecurityStampChanged = 4
}

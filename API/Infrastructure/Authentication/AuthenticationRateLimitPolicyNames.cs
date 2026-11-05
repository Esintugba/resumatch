namespace Api.Infrastructure.Authentication;

public static class AuthenticationRateLimitPolicyNames
{
    public const string Login = "auth-login";
    public const string Register = "auth-register";
    public const string Refresh = "auth-refresh";
    public const string Logout = "auth-logout";
}

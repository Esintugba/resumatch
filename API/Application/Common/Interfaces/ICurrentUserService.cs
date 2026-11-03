namespace Api.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? SessionId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }

    Guid GetRequiredUserId();
}

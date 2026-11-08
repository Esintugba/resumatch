namespace Api.DTOs.Resume;

public sealed class ResumeContactResponse
{
    public Guid Id { get; init; }
    /// <summary>Send as If-Match to update or delete the contact.</summary>
    public string ETag { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public string? PostalCode { get; init; }
}

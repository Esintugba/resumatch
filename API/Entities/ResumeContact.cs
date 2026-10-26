namespace Api.Entities;

public class ResumeContact : BaseEntity
{
    public Guid ResumeId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }

    public Resume Resume { get; set; } = null!;
}

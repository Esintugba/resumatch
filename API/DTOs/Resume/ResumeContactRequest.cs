using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Resume;

public sealed class ResumeContactRequest
{
    [Required]
    [StringLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; init; } = string.Empty;

    [StringLength(30)]
    public string? PhoneNumber { get; init; }

    [StringLength(100)]
    public string? City { get; init; }

    [StringLength(100)]
    public string? Country { get; init; }

    [StringLength(20)]
    public string? PostalCode { get; init; }
}

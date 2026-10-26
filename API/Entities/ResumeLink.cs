using Api.Entities.Enums;

namespace Api.Entities;

public class ResumeLink : BaseEntity
{
    public Guid ResumeId { get; set; }

    public ResumeLinkType Type { get; set; }

    public string? Label { get; set; }
    public string Url { get; set; } = string.Empty;

    public Resume Resume { get; set; } = null!;
}

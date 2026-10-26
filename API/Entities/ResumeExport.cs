using Api.Entities.Enums;

namespace Api.Entities;

public class ResumeExport : BaseEntity
{
    public Guid ResumeId { get; set; }

    public ResumeExportFormat Format { get; set; }

    public string TemplateKey { get; set; } = string.Empty;

    public string? FileUrl { get; set; }

    public DateTimeOffset ExportedAt { get; set; }

    public Resume Resume { get; set; } = null!;
}

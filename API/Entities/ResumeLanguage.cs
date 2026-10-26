using Api.Entities.Enums;

namespace Api.Entities;

public class ResumeLanguage : BaseEntity
{
    public Guid ResumeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public LanguageProficiency Proficiency { get; set; }

    public int SortOrder { get; set; }

    public Resume Resume { get; set; } = null!;
}

namespace Api.Entities;

public class JobPostingParseDraftSkillEvidence : BaseEntity
{
    public Guid JobPostingParseDraftSkillId { get; set; }

    public string Text { get; set; } = string.Empty;
    public int Start { get; set; }
    public int Length { get; set; }

    public JobPostingParseDraftSkill JobPostingParseDraftSkill { get; set; } =
        null!;
}

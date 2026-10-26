namespace Api.Entities.Enums;

public enum JobPostingSkillProvenanceStatus
{
    Manual = 0,
    AiActive = 1,
    AiStale = 2,
    AiManuallyAdopted = 3,
    LegacyAiUnknown = 4
}

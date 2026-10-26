using Api.Entities.Enums;

namespace Api.Application.JobParsing.Engine.Validation;

public static class RequirementImportancePolicy
{
    public static int GetProposedScore(
        JobSkillRequirementType requirementType)
    {
        return requirementType switch
        {
            JobSkillRequirementType.Required => 100,
            JobSkillRequirementType.Preferred => 60,
            JobSkillRequirementType.Mentioned => 30,
            _ => throw new ArgumentOutOfRangeException(
                nameof(requirementType),
                "Skill requirement type is not defined.")
        };
    }
}

using Api.Application.JobParsing.Models;

namespace Api.Application.JobParsing;

public interface ISkillCanonicalizer
{
    SkillCanonicalizationResult Resolve(
        ExtractedSkillCandidate candidate,
        IReadOnlyCollection<SkillCatalogItem> skillCatalog);
}

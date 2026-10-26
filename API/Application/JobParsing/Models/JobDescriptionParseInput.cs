using System.Collections.Immutable;

namespace Api.Application.JobParsing.Models;

public sealed record JobDescriptionParseInput
{
    public JobDescriptionParseInput(
        Guid jobPostingId,
        string title,
        string description,
        string descriptionHash,
        uint sourceJobPostingVersion,
        DateTimeOffset asOf,
        ImmutableArray<SkillCatalogItem> skillCatalog)
    {
        if (jobPostingId == Guid.Empty)
        {
            throw new ArgumentException(
                "Job posting identifier cannot be empty.",
                nameof(jobPostingId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "Job posting title is required.",
                nameof(title));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Job posting description is required.",
                nameof(description));
        }

        if (string.IsNullOrWhiteSpace(descriptionHash))
        {
            throw new ArgumentException(
                "Description hash is required.",
                nameof(descriptionHash));
        }

        if (sourceJobPostingVersion == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceJobPostingVersion),
                "Source job posting version must be greater than zero.");
        }

        if (asOf == default)
        {
            throw new ArgumentException(
                "Parse timestamp must be specified.",
                nameof(asOf));
        }

        SkillCatalog = skillCatalog.IsDefault
            ? ImmutableArray<SkillCatalogItem>.Empty
            : skillCatalog;

        if (SkillCatalog.Any(skill => skill is null))
        {
            throw new ArgumentException(
                "Skill catalog cannot contain null values.",
                nameof(skillCatalog));
        }

        if (SkillCatalog.Select(skill => skill.Id).Distinct().Count() !=
            SkillCatalog.Length)
        {
            throw new ArgumentException(
                "Skill catalog cannot contain duplicate skill identifiers.",
                nameof(skillCatalog));
        }

        JobPostingId = jobPostingId;
        Title = title;
        Description = description;
        DescriptionHash = descriptionHash;
        SourceJobPostingVersion = sourceJobPostingVersion;
        AsOf = asOf;
    }

    public Guid JobPostingId { get; }
    public string Title { get; }
    public string Description { get; }
    public string DescriptionHash { get; }
    public uint SourceJobPostingVersion { get; }
    public DateTimeOffset AsOf { get; }
    public ImmutableArray<SkillCatalogItem> SkillCatalog { get; }
}

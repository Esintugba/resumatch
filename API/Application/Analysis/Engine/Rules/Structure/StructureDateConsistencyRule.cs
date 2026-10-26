using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;
using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Rules.Structure;

public sealed class StructureDateConsistencyRule : IAnalysisRule
{
    public const string Code = "ATS.STR.003";

    public string RuleCode => Code;
    public RuleCategory Category => RuleCategory.Structure;
    public decimal Weight => 30m;
    public RuleIssueMetadata FailureIssue { get; } = new(
        AnalysisIssueType.Structure,
        IssueSeverity.Warning,
        "Experience/Education/Projects/Certifications");

    public decimal? Evaluate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applicableCount = 0;
        var validCount = 0;
        var resume = context.Resume;

        if (!resume.Experiences.IsDefaultOrEmpty)
        {
            foreach (var experience in resume.Experiences)
            {
                applicableCount++;
                if (IsValid(experience))
                {
                    validCount++;
                }
            }
        }

        EvaluateOptionalDateRecords(
            resume.Educations,
            education => education.StartDate,
            education => education.EndDate,
            ref applicableCount,
            ref validCount);

        EvaluateOptionalDateRecords(
            resume.Projects,
            project => project.StartDate,
            project => project.EndDate,
            ref applicableCount,
            ref validCount);

        EvaluateOptionalDateRecords(
            resume.Certifications,
            certification => certification.IssueDate,
            certification => certification.ExpirationDate,
            ref applicableCount,
            ref validCount);

        return applicableCount == 0
            ? null
            : (decimal)validCount / applicableCount;
    }

    private static bool IsValid(ExperienceSnapshot experience)
    {
        if (experience.IsCurrent && experience.EndDate.HasValue)
        {
            return false;
        }

        return !experience.EndDate.HasValue ||
               experience.EndDate.Value >= experience.StartDate;
    }

    private static void EvaluateOptionalDateRecords<TRecord>(
        ImmutableArray<TRecord> records,
        Func<TRecord, DateOnly?> getStartDate,
        Func<TRecord, DateOnly?> getEndDate,
        ref int applicableCount,
        ref int validCount)
    {
        if (records.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var record in records)
        {
            var startDate = getStartDate(record);
            var endDate = getEndDate(record);

            if (!startDate.HasValue && !endDate.HasValue)
            {
                continue;
            }

            applicableCount++;
            if (!startDate.HasValue ||
                !endDate.HasValue ||
                endDate.Value >= startDate.Value)
            {
                validCount++;
            }
        }
    }
}

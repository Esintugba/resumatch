namespace Api.Application.Analysis.Engine.Rules.Formatting;

internal static class ResumeTextValues
{
    public static IEnumerable<string> Enumerate(RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resume = context.Resume;

        foreach (var value in Present(
                     resume.Title,
                     resume.ProfessionalTitle,
                     resume.Summary,
                     resume.TargetRole))
        {
            yield return value;
        }

        if (resume.Contact is not null)
        {
            foreach (var value in Present(
                         resume.Contact.FirstName,
                         resume.Contact.LastName,
                         resume.Contact.Email,
                         resume.Contact.PhoneNumber,
                         resume.Contact.City,
                         resume.Contact.Country,
                         resume.Contact.PostalCode))
            {
                yield return value;
            }
        }

        if (!resume.Links.IsDefaultOrEmpty)
        {
            foreach (var link in resume.Links)
            {
                foreach (var value in Present(link.Label, link.Url))
                {
                    yield return value;
                }
            }
        }

        if (!resume.Experiences.IsDefaultOrEmpty)
        {
            foreach (var experience in resume.Experiences)
            {
                foreach (var value in Present(
                             experience.CompanyName,
                             experience.Position,
                             experience.Location,
                             experience.Description))
                {
                    yield return value;
                }

                if (experience.Bullets.IsDefaultOrEmpty)
                {
                    continue;
                }

                foreach (var bullet in experience.Bullets)
                {
                    if (RuleText.IsUsableEvidence(bullet))
                    {
                        yield return bullet.Content;
                    }
                }
            }
        }

        if (!resume.Educations.IsDefaultOrEmpty)
        {
            foreach (var education in resume.Educations)
            {
                foreach (var value in Present(
                             education.InstitutionName,
                             education.Degree,
                             education.FieldOfStudy,
                             education.Location,
                             education.GradeScale,
                             education.Description))
                {
                    yield return value;
                }
            }
        }

        if (!resume.Skills.IsDefaultOrEmpty)
        {
            foreach (var resumeSkill in resume.Skills)
            {
                if (RuleText.HasContent(resumeSkill.Skill.Name))
                {
                    yield return resumeSkill.Skill.Name;
                }
            }
        }

        if (!resume.Projects.IsDefaultOrEmpty)
        {
            foreach (var project in resume.Projects)
            {
                foreach (var value in Present(
                             project.Name,
                             project.Description,
                             project.Url,
                             project.RepositoryUrl))
                {
                    yield return value;
                }
            }
        }

        if (!resume.Certifications.IsDefaultOrEmpty)
        {
            foreach (var certification in resume.Certifications)
            {
                foreach (var value in Present(
                             certification.Name,
                             certification.IssuingOrganization,
                             certification.CredentialId,
                             certification.CredentialUrl))
                {
                    yield return value;
                }
            }
        }

        if (!resume.Languages.IsDefaultOrEmpty)
        {
            foreach (var language in resume.Languages)
            {
                if (RuleText.HasContent(language.Name))
                {
                    yield return language.Name;
                }
            }
        }
    }

    private static IEnumerable<string> Present(params string?[] values)
    {
        return values
            .Where(RuleText.HasContent)
            .Select(value => value!);
    }
}

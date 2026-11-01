using Api.DTOs.Resume;
using Api.Entities;
using Api.Application.Common.Concurrency;

namespace Api.Application.Mapping;

public static class ResumeMappings
{
    public static Resume ToEntity(this CreateResumeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new Resume
        {
            Title = request.Title,
            ProfessionalTitle = request.ProfessionalTitle,
            Summary = request.Summary,
            UnlistedSkillsNote = request.UnlistedSkillsNote,
            IsMasterResume = request.IsMasterResume,
            SourceResumeId = request.SourceResumeId,
            TemplateKey = request.TemplateKey,
            TargetRole = request.TargetRole,
            Contact = request.Contact?.ToEntity(),
            Links = request.Links
                .Select(link => link.ToEntity())
                .ToList()
        };
    }

    public static void ApplyUpdate(this UpdateResumeRequest request, Resume resume)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resume);

        resume.Title = request.Title;
        resume.ProfessionalTitle = request.ProfessionalTitle;
        resume.Summary = request.Summary;
        resume.UnlistedSkillsNote = request.UnlistedSkillsNote;
        resume.TemplateKey = request.TemplateKey;
        resume.TargetRole = request.TargetRole;
    }

    public static ResumeResponse ToResponse(this Resume resume)
    {
        ArgumentNullException.ThrowIfNull(resume);

        return new ResumeResponse
        {
            Id = resume.Id,
            ETag = EntityTagCodec.Format(resume.Version),
            Title = resume.Title,
            ProfessionalTitle = resume.ProfessionalTitle,
            Summary = resume.Summary,
            UnlistedSkillsNote = resume.UnlistedSkillsNote,
            IsMasterResume = resume.IsMasterResume,
            SourceResumeId = resume.SourceResumeId,
            TemplateKey = resume.TemplateKey,
            TargetRole = resume.TargetRole,
            IsArchived = resume.IsArchived,
            CreatedAt = resume.CreatedAt,
            UpdatedAt = resume.UpdatedAt,
            Contact = resume.Contact?.ToResponse(),
            Links = resume.Links
                .OrderBy(link => link.Id)
                .Select(link => link.ToResponse())
                .ToList()
        };
    }

    public static ResumeListItemResponse ToListItemResponse(this Resume resume)
    {
        ArgumentNullException.ThrowIfNull(resume);

        return new ResumeListItemResponse
        {
            Id = resume.Id,
            ETag = EntityTagCodec.Format(resume.Version),
            Title = resume.Title,
            ProfessionalTitle = resume.ProfessionalTitle,
            TargetRole = resume.TargetRole,
            IsMasterResume = resume.IsMasterResume,
            IsArchived = resume.IsArchived,
            CreatedAt = resume.CreatedAt,
            UpdatedAt = resume.UpdatedAt
        };
    }

    public static ResumeEditorResponse ToEditorResponse(this Resume resume)
    {
        ArgumentNullException.ThrowIfNull(resume);

        return new ResumeEditorResponse
        {
            Id = resume.Id,
            ETag = EntityTagCodec.Format(resume.Version),
            Title = resume.Title,
            ProfessionalTitle = resume.ProfessionalTitle,
            Summary = resume.Summary,
            UnlistedSkillsNote = resume.UnlistedSkillsNote,
            IsMasterResume = resume.IsMasterResume,
            SourceResumeId = resume.SourceResumeId,
            TemplateKey = resume.TemplateKey,
            TargetRole = resume.TargetRole,
            IsArchived = resume.IsArchived,
            LastExportedAt = resume.LastExportedAt,
            CreatedAt = resume.CreatedAt,
            UpdatedAt = resume.UpdatedAt,
            Contact = resume.Contact?.ToResponse(),
            Links = resume.Links
                .OrderBy(link => link.Id)
                .Select(link => link.ToResponse())
                .ToList(),
            Experiences = resume.Experiences
                .OrderBy(experience => experience.SortOrder)
                .ThenByDescending(experience => experience.StartDate)
                .ThenBy(experience => experience.Id)
                .Select(experience => experience.ToResponse())
                .ToList(),
            Educations = resume.Educations
                .OrderBy(education => education.SortOrder)
                .ThenByDescending(education => education.EndDate)
                .ThenByDescending(education => education.StartDate)
                .ThenBy(education => education.Id)
                .Select(education => education.ToResponse())
                .ToList(),
            Skills = resume.Skills
                .OrderBy(resumeSkill => resumeSkill.SortOrder)
                .ThenBy(resumeSkill => resumeSkill.Skill.NormalizedName)
                .ThenBy(resumeSkill => resumeSkill.SkillId)
                .Select(resumeSkill => resumeSkill.ToResponse())
                .ToList(),
            Projects = resume.Projects
                .OrderBy(project => project.SortOrder)
                .ThenByDescending(project => project.EndDate)
                .ThenByDescending(project => project.StartDate)
                .ThenBy(project => project.Id)
                .Select(project => project.ToResponse())
                .ToList(),
            Certifications = resume.Certifications
                .OrderBy(certification => certification.SortOrder)
                .ThenByDescending(certification => certification.IssueDate)
                .ThenBy(certification => certification.Id)
                .Select(certification => certification.ToResponse())
                .ToList(),
            Languages = resume.Languages
                .OrderBy(language => language.SortOrder)
                .ThenBy(language => language.Name)
                .ThenBy(language => language.Id)
                .Select(language => language.ToResponse())
                .ToList()
        };
    }

    public static ResumeContact ToEntity(this ResumeContactRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResumeContact
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            City = request.City,
            Country = request.Country,
            PostalCode = request.PostalCode
        };
    }

    public static ResumeContactResponse ToResponse(this ResumeContact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ResumeContactResponse
        {
            Id = contact.Id,
            FirstName = contact.FirstName,
            LastName = contact.LastName,
            Email = contact.Email,
            PhoneNumber = contact.PhoneNumber,
            City = contact.City,
            Country = contact.Country,
            PostalCode = contact.PostalCode
        };
    }

    public static void ApplyUpdate(
        this ResumeContactRequest request,
        ResumeContact contact)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(contact);

        contact.FirstName = request.FirstName;
        contact.LastName = request.LastName;
        contact.Email = request.Email;
        contact.PhoneNumber = request.PhoneNumber;
        contact.City = request.City;
        contact.Country = request.Country;
        contact.PostalCode = request.PostalCode;
    }

    public static ResumeLink ToEntity(this ResumeLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResumeLink
        {
            Type = request.Type!.Value,
            Label = request.Label,
            Url = request.Url
        };
    }

    public static ResumeLinkResponse ToResponse(this ResumeLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return new ResumeLinkResponse
        {
            Id = link.Id,
            Type = link.Type,
            Label = link.Label,
            Url = link.Url
        };
    }

    public static void ApplyUpdate(
        this ResumeLinkRequest request,
        ResumeLink link)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(link);

        link.Type = request.Type!.Value;
        link.Label = request.Label;
        link.Url = request.Url;
    }
}

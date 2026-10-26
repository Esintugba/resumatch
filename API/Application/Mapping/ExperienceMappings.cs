using Api.DTOs.Experience;
using Api.Entities;
using Api.Application.Common.Concurrency;

namespace Api.Application.Mapping;

public static class ExperienceMappings
{
    public static ResumeExperience ToEntity(this CreateExperienceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResumeExperience
        {
            CompanyName = request.CompanyName,
            Position = request.Position,
            Location = request.Location,
            EmploymentType = request.EmploymentType!.Value,
            StartDate = request.StartDate!.Value,
            EndDate = request.EndDate,
            IsCurrent = request.IsCurrent,
            Description = request.Description,
            SortOrder = request.SortOrder,
            Bullets = request.Bullets
                .Select(bullet => bullet.ToEntity())
                .ToList()
        };
    }

    public static void ApplyUpdate(
        this UpdateExperienceRequest request,
        ResumeExperience experience)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(experience);

        experience.CompanyName = request.CompanyName;
        experience.Position = request.Position;
        experience.Location = request.Location;
        experience.EmploymentType = request.EmploymentType!.Value;
        experience.StartDate = request.StartDate!.Value;
        experience.EndDate = request.EndDate;
        experience.IsCurrent = request.IsCurrent;
        experience.Description = request.Description;
        experience.SortOrder = request.SortOrder;
    }

    public static ExperienceResponse ToResponse(this ResumeExperience experience)
    {
        ArgumentNullException.ThrowIfNull(experience);

        return new ExperienceResponse
        {
            Id = experience.Id,
            ETag = EntityTagCodec.Format(experience.Version),
            CompanyName = experience.CompanyName,
            Position = experience.Position,
            Location = experience.Location,
            EmploymentType = experience.EmploymentType,
            StartDate = experience.StartDate,
            EndDate = experience.EndDate,
            IsCurrent = experience.IsCurrent,
            Description = experience.Description,
            SortOrder = experience.SortOrder,
            Bullets = experience.Bullets
                .OrderBy(bullet => bullet.SortOrder)
                .ThenBy(bullet => bullet.Id)
                .Select(bullet => bullet.ToResponse())
                .ToList()
        };
    }

    public static ExperienceBullet ToEntity(this ExperienceBulletRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ExperienceBullet
        {
            Content = request.Content,
            SortOrder = request.SortOrder
        };
    }

    public static ExperienceBullet ToEntity(this UpdateExperienceBulletRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ExperienceBullet
        {
            Content = request.Content,
            SortOrder = request.SortOrder
        };
    }

    public static ExperienceBulletResponse ToResponse(this ExperienceBullet bullet)
    {
        ArgumentNullException.ThrowIfNull(bullet);

        return new ExperienceBulletResponse
        {
            Id = bullet.Id,
            Content = bullet.Content,
            SortOrder = bullet.SortOrder,
            IsAiGenerated = bullet.IsAiGenerated
        };
    }
}

using Api.DTOs.Education;
using Api.Entities;

namespace Api.Application.Mapping;

public static class EducationMappings
{
    public static ResumeEducation ToEntity(this CreateEducationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResumeEducation
        {
            InstitutionName = request.InstitutionName,
            Degree = request.Degree,
            FieldOfStudy = request.FieldOfStudy,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Location = request.Location,
            Grade = request.Grade,
            GradeScale = request.GradeScale,
            Description = request.Description,
            SortOrder = request.SortOrder
        };
    }

    public static void ApplyUpdate(
        this UpdateEducationRequest request,
        ResumeEducation education)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(education);

        education.InstitutionName = request.InstitutionName;
        education.Degree = request.Degree;
        education.FieldOfStudy = request.FieldOfStudy;
        education.StartDate = request.StartDate;
        education.EndDate = request.EndDate;
        education.Location = request.Location;
        education.Grade = request.Grade;
        education.GradeScale = request.GradeScale;
        education.Description = request.Description;
        education.SortOrder = request.SortOrder;
    }

    public static EducationResponse ToResponse(this ResumeEducation education)
    {
        ArgumentNullException.ThrowIfNull(education);

        return new EducationResponse
        {
            Id = education.Id,
            InstitutionName = education.InstitutionName,
            Degree = education.Degree,
            FieldOfStudy = education.FieldOfStudy,
            StartDate = education.StartDate,
            EndDate = education.EndDate,
            Location = education.Location,
            Grade = education.Grade,
            GradeScale = education.GradeScale,
            Description = education.Description,
            SortOrder = education.SortOrder
        };
    }
}

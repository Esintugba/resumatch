using Api.Entities.Enums;

namespace Api.Application.Analysis.Engine.Models;

public sealed record SkillSnapshot(
    Guid Id,
    string Name,
    string NormalizedName,
    SkillCategory Category);

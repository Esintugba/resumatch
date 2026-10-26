using Api.Application.Experiences;
using Api.DTOs.Experience;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/{resumeId:guid}/experiences")]
public sealed class ResumeExperiencesController : ControllerBase
{
    private readonly IExperienceService _experienceService;

    public ResumeExperiencesController(IExperienceService experienceService)
    {
        _experienceService = experienceService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ExperienceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ExperienceResponse>>> GetAll(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _experienceService.GetExperiencesAsync(
            resumeId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ExperienceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExperienceResponse>> Create(
        Guid resumeId,
        [FromBody] CreateExperienceRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _experienceService.CreateAsync(
            resumeId,
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(
            nameof(GetById),
            new { resumeId, experienceId = response.Id },
            response);
    }

    [HttpGet("{experienceId:guid}")]
    [ProducesResponseType(typeof(ExperienceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExperienceResponse>> GetById(
        Guid resumeId,
        Guid experienceId,
        CancellationToken cancellationToken)
    {
        var response = await _experienceService.GetByIdAsync(
            resumeId,
            experienceId,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpPut("{experienceId:guid}")]
    [ProducesResponseType(typeof(ExperienceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<ExperienceResponse>> Update(
        Guid resumeId,
        Guid experienceId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] UpdateExperienceRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _experienceService.UpdateAsync(
            resumeId,
            experienceId,
            expectedVersion,
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpDelete("{experienceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        Guid experienceId,
        CancellationToken cancellationToken)
    {
        await _experienceService.DeleteAsync(
            resumeId,
            experienceId,
            cancellationToken);

        return NoContent();
    }
}

using Api.Application.Educations;
using Api.DTOs.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/{resumeId:guid}/educations")]
public sealed class ResumeEducationsController : ControllerBase
{
    private readonly IEducationService _educationService;

    public ResumeEducationsController(IEducationService educationService)
    {
        _educationService = educationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EducationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EducationResponse>>> GetAll(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _educationService.GetEducationsAsync(
            resumeId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(EducationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EducationResponse>> Create(
        Guid resumeId,
        [FromBody] CreateEducationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _educationService.CreateAsync(
            resumeId,
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { resumeId, educationId = response.Id },
            response);
    }

    [HttpGet("{educationId:guid}")]
    [ProducesResponseType(typeof(EducationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EducationResponse>> GetById(
        Guid resumeId,
        Guid educationId,
        CancellationToken cancellationToken)
    {
        var response = await _educationService.GetByIdAsync(
            resumeId,
            educationId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPut("{educationId:guid}")]
    [ProducesResponseType(typeof(EducationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EducationResponse>> Update(
        Guid resumeId,
        Guid educationId,
        [FromBody] UpdateEducationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _educationService.UpdateAsync(
            resumeId,
            educationId,
            request,
            cancellationToken);

        return Ok(response);
    }

    [HttpDelete("{educationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        Guid educationId,
        CancellationToken cancellationToken)
    {
        await _educationService.DeleteAsync(
            resumeId,
            educationId,
            cancellationToken);

        return NoContent();
    }
}

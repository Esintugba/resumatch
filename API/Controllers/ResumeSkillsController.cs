using Api.Application.Skills;
using Api.DTOs.Skill;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/{resumeId:guid}/skills")]
public sealed class ResumeSkillsController : ControllerBase
{
    private readonly ISkillService _skillService;

    public ResumeSkillsController(ISkillService skillService)
    {
        _skillService = skillService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ResumeSkillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ResumeSkillResponse>>> GetAll(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _skillService.GetResumeSkillsAsync(
            resumeId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ResumeSkillResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResumeSkillResponse>> Create(
        Guid resumeId,
        [FromBody] AddResumeSkillRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _skillService.AddResumeSkillAsync(
            resumeId,
            request,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPut("{skillId:guid}")]
    [ProducesResponseType(typeof(ResumeSkillResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeSkillResponse>> Update(
        Guid resumeId,
        Guid skillId,
        [FromBody] UpdateResumeSkillRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _skillService.UpdateResumeSkillAsync(
            resumeId,
            skillId,
            request,
            cancellationToken);

        return Ok(response);
    }

    [HttpDelete("{skillId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        Guid skillId,
        CancellationToken cancellationToken)
    {
        await _skillService.DeleteResumeSkillAsync(
            resumeId,
            skillId,
            cancellationToken);

        return NoContent();
    }
}

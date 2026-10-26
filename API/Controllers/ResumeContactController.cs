using Api.Application.ResumeContacts;
using Api.DTOs.Resume;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/{resumeId:guid}/contact")]
public sealed class ResumeContactController : ControllerBase
{
    private readonly IResumeContactService _contactService;

    public ResumeContactController(IResumeContactService contactService)
    {
        _contactService = contactService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ResumeContactResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeContactResponse>> Get(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _contactService.GetAsync(resumeId, cancellationToken);
        return Ok(response);
    }

    [HttpPut]
    [ProducesResponseType(typeof(ResumeContactResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeContactResponse>> Upsert(
        Guid resumeId,
        [FromBody] ResumeContactRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _contactService.UpsertAsync(
            resumeId,
            request,
            cancellationToken);

        if (result.IsCreated)
        {
            return CreatedAtAction(
                nameof(Get),
                new { resumeId },
                result.Contact);
        }

        return Ok(result.Contact);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        await _contactService.DeleteAsync(resumeId, cancellationToken);
        return NoContent();
    }
}

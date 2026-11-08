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
        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    /// <summary>
    /// Creates the contact when the resume has none (send no If-Match), or
    /// updates it (If-Match with its ETag is required: 428 without, 412 when stale).
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ResumeContactResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResumeContactResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<ResumeContactResponse>> Upsert(
        Guid resumeId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] ResumeContactRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _contactService.UpsertAsync(
            resumeId,
            ifMatch,
            request,
            cancellationToken);
        Response.Headers.ETag = result.Contact.ETag;

        if (result.IsCreated)
        {
            return CreatedAtAction(
                nameof(Get),
                new { resumeId },
                result.Contact);
        }

        return Ok(result.Contact);
    }

    /// <summary>Deletes the contact. If-Match with its ETag is required: 428 without, 412 when stale.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        await _contactService.DeleteAsync(resumeId, ifMatch, cancellationToken);
        return NoContent();
    }
}

using Api.Application.ResumeLinks;
using Api.DTOs.Resume;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/{resumeId:guid}/links")]
public sealed class ResumeLinksController : ControllerBase
{
    private readonly IResumeLinkService _linkService;

    public ResumeLinksController(IResumeLinkService linkService)
    {
        _linkService = linkService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ResumeLinkResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ResumeLinkResponse>>> GetAll(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _linkService.GetLinksAsync(resumeId, cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ResumeLinkResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeLinkResponse>> Create(
        Guid resumeId,
        [FromBody] ResumeLinkRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _linkService.CreateAsync(
            resumeId,
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { resumeId, linkId = response.Id },
            response);
    }

    [HttpGet("{linkId:guid}")]
    [ProducesResponseType(typeof(ResumeLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeLinkResponse>> GetById(
        Guid resumeId,
        Guid linkId,
        CancellationToken cancellationToken)
    {
        var response = await _linkService.GetByIdAsync(
            resumeId,
            linkId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPut("{linkId:guid}")]
    [ProducesResponseType(typeof(ResumeLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeLinkResponse>> Update(
        Guid resumeId,
        Guid linkId,
        [FromBody] ResumeLinkRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _linkService.UpdateAsync(
            resumeId,
            linkId,
            request,
            cancellationToken);

        return Ok(response);
    }

    [HttpDelete("{linkId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        Guid linkId,
        CancellationToken cancellationToken)
    {
        await _linkService.DeleteAsync(resumeId, linkId, cancellationToken);
        return NoContent();
    }
}

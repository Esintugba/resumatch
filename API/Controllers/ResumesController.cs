using Api.Application.Resumes;
using Api.DTOs.Common;
using Api.DTOs.Resume;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes")]
public sealed class ResumesController : ControllerBase
{
    private readonly IResumeService _resumeService;

    public ResumesController(IResumeService resumeService)
    {
        _resumeService = resumeService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ResumeResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ResumeResponse>> Create(
        [FromBody] CreateResumeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _resumeService.CreateAsync(request, cancellationToken);
        Response.Headers.ETag = response.ETag;

        return CreatedAtAction(
            nameof(GetById),
            new { resumeId = response.Id },
            response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ResumeListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<ResumeListItemResponse>>> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var response = await _resumeService.GetMyResumesAsync(
            page,
            pageSize,
            cancellationToken);
        return Ok(response);
    }

    [HttpGet("{resumeId:guid}")]
    [ProducesResponseType(typeof(ResumeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeResponse>> GetById(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _resumeService.GetByIdAsync(resumeId, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpGet("{resumeId:guid}/editor")]
    [ProducesResponseType(typeof(ResumeEditorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeEditorResponse>> GetEditor(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _resumeService.GetEditorAsync(
            resumeId,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpPut("{resumeId:guid}")]
    [ProducesResponseType(typeof(ResumeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<ResumeResponse>> Update(
        Guid resumeId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] UpdateResumeRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _resumeService.UpdateAsync(
            resumeId,
            expectedVersion,
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpPatch("{resumeId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        await _resumeService.ArchiveAsync(resumeId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{resumeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        await _resumeService.DeleteAsync(resumeId, cancellationToken);
        return NoContent();
    }
}

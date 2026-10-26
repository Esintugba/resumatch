using Api.Application.Projects;
using Api.DTOs.Project;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/{resumeId:guid}/projects")]
public sealed class ResumeProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;

    public ResumeProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> GetAll(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _projectService.GetProjectsAsync(
            resumeId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> Create(
        Guid resumeId,
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _projectService.CreateAsync(
            resumeId,
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(
            nameof(GetById),
            new { resumeId, projectId = response.Id },
            response);
    }

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> GetById(
        Guid resumeId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var response = await _projectService.GetByIdAsync(
            resumeId,
            projectId,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpPut("{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<ProjectResponse>> Update(
        Guid resumeId,
        Guid projectId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _projectService.UpdateAsync(
            resumeId,
            projectId,
            expectedVersion,
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpDelete("{projectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await _projectService.DeleteAsync(
            resumeId,
            projectId,
            cancellationToken);

        return NoContent();
    }
}

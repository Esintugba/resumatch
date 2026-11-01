using Api.Application.JobPostings;
using Api.DTOs.Common;
using Api.DTOs.JobPosting;
using Api.Entities.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/job-postings")]
public sealed class JobPostingsController : ControllerBase
{
    private readonly IJobPostingService _jobPostingService;

    public JobPostingsController(IJobPostingService jobPostingService)
    {
        _jobPostingService = jobPostingService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(JobPostingResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<JobPostingResponse>> Create(
        [FromBody] CreateJobPostingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _jobPostingService.CreateAsync(
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(
            nameof(GetById),
            new { jobPostingId = response.Id },
            response);
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(PagedResponse<JobPostingListItemResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<JobPostingListItemResponse>>> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] JobPostingStatus? status = null)
    {
        var response = await _jobPostingService.GetMyJobPostingsAsync(
            page,
            pageSize,
            search,
            status,
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{jobPostingId:guid}")]
    [ProducesResponseType(typeof(JobPostingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobPostingResponse>> GetById(
        Guid jobPostingId,
        CancellationToken cancellationToken)
    {
        var response = await _jobPostingService.GetByIdAsync(
            jobPostingId,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpPut("{jobPostingId:guid}")]
    [ProducesResponseType(typeof(JobPostingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<JobPostingResponse>> Update(
        Guid jobPostingId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] UpdateJobPostingRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _jobPostingService.UpdateAsync(
            jobPostingId,
            expectedVersion,
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    /// <summary>Adds a catalog skill to a job posting by hand.</summary>
    /// <remarks>If-Match must contain the current job posting ETag.</remarks>
    [HttpPost("{jobPostingId:guid}/skills")]
    [ProducesResponseType(typeof(JobPostingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<JobPostingResponse>> AddSkill(
        Guid jobPostingId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] AddJobPostingSkillRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _jobPostingService.AddSkillAsync(
            jobPostingId,
            expectedVersion,
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    /// <summary>Removes a manually added skill from a job posting.</summary>
    /// <remarks>If-Match must contain the current job posting ETag.</remarks>
    [HttpDelete("{jobPostingId:guid}/skills/{skillId:guid}")]
    [ProducesResponseType(typeof(JobPostingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<JobPostingResponse>> RemoveSkill(
        Guid jobPostingId,
        Guid skillId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _jobPostingService.RemoveSkillAsync(
            jobPostingId,
            skillId,
            expectedVersion,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpPost("{jobPostingId:guid}/skills/{skillId:guid}/adopt")]
    [ProducesResponseType(typeof(JobPostingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<JobPostingResponse>> AdoptSkill(
        Guid jobPostingId,
        Guid skillId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _jobPostingService.AdoptSkillAsync(
            jobPostingId,
            skillId,
            expectedVersion,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpDelete("{jobPostingId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Delete(
        Guid jobPostingId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        await _jobPostingService.DeleteAsync(
            jobPostingId,
            expectedVersion,
            cancellationToken);

        return NoContent();
    }
}

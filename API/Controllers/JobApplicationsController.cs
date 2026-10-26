using Api.Application.JobApplications;
using Api.DTOs.Common;
using Api.DTOs.JobApplication;
using Api.Entities.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/job-applications")]
public sealed class JobApplicationsController : ControllerBase
{
    private readonly IJobApplicationService _jobApplicationService;

    public JobApplicationsController(
        IJobApplicationService jobApplicationService)
    {
        _jobApplicationService = jobApplicationService;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(JobApplicationResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JobApplicationResponse>> Create(
        [FromBody] CreateJobApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _jobApplicationService.CreateAsync(
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(
            nameof(GetById),
            new { id = response.Id },
            response);
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(PagedResponse<JobApplicationListItemResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<JobApplicationListItemResponse>>> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ApplicationStatus? status = null,
        [FromQuery] Guid? jobPostingId = null)
    {
        var response = await _jobApplicationService.GetMyJobApplicationsAsync(
            page,
            pageSize,
            status,
            jobPostingId,
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(JobApplicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobApplicationResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await _jobApplicationService.GetByIdAsync(
            id,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(JobApplicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<JobApplicationResponse>> Update(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] UpdateJobApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _jobApplicationService.UpdateAsync(
            id,
            expectedVersion,
            request,
            cancellationToken);

        Response.Headers.ETag = response.ETag;
        return Ok(response);
    }
}

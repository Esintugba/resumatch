using Api.Application.Common.Concurrency;
using Api.Application.JobParsing;
using Api.DTOs.Common;
using Api.DTOs.JobPosting;
using Api.Entities.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/job-postings/{jobPostingId:guid}")]
public sealed class JobPostingParseDraftsController : ControllerBase
{
    private readonly IJobPostingParseDraftService _draftService;

    public JobPostingParseDraftsController(
        IJobPostingParseDraftService draftService)
    {
        _draftService = draftService ??
            throw new ArgumentNullException(nameof(draftService));
    }

    /// <summary>
    /// Requests asynchronous parsing or returns the matching active draft.
    /// </summary>
    /// <remarks>
    /// If-Match must contain the current JobPosting ETag. This operation does
    /// not wait for an extraction provider to finish.
    /// </remarks>
    [HttpPost("parse")]
    [EnableRateLimiting(JobParsingRateLimitPolicyNames.RequestParse)]
    [ProducesResponseType(
        typeof(JobPostingParseDraftResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(JobPostingParseDraftResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<JobPostingParseDraftResponse>> RequestParse(
        Guid jobPostingId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _draftService.RequestParseAsync(
            jobPostingId,
            expectedVersion,
            cancellationToken);

        Response.Headers.ETag = EntityTagCodec.Format(response.DraftVersion);
        var routeValues = new
        {
            jobPostingId,
            draftId = response.DraftId
        };

        if (response.Status == JobPostingParseDraftStatus.Ready)
        {
            Response.Headers.Location = Url.ActionLink(
                nameof(GetDraft),
                values: routeValues);
            return Ok(response);
        }

        return AcceptedAtAction(nameof(GetDraft), routeValues, response);
    }

    [HttpGet("parse-drafts/{draftId:guid}")]
    [ProducesResponseType(
        typeof(JobPostingParseDraftDetailResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobPostingParseDraftDetailResponse>> GetDraft(
        Guid jobPostingId,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var response = await _draftService.GetDraftAsync(
            jobPostingId,
            draftId,
            cancellationToken);

        Response.Headers.ETag = EntityTagCodec.Format(response.DraftVersion);
        return Ok(response);
    }

    [HttpGet("parse-drafts")]
    [ProducesResponseType(
        typeof(PagedResponse<JobPostingParseDraftListItemResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        PagedResponse<JobPostingParseDraftListItemResponse>>> GetHistory(
        Guid jobPostingId,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] JobPostingParseDraftStatus? status = null)
    {
        var response = await _draftService.GetDraftHistoryAsync(
            jobPostingId,
            page,
            pageSize,
            status,
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Cancels a cancellable parse draft.</summary>
    /// <remarks>If-Match must contain the current draft ETag.</remarks>
    [HttpPost("parse-drafts/{draftId:guid}/cancel")]
    [ProducesResponseType(
        typeof(JobPostingParseDraftResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<JobPostingParseDraftResponse>> Cancel(
        Guid jobPostingId,
        Guid draftId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _draftService.CancelDraftAsync(
            jobPostingId,
            draftId,
            expectedVersion,
            cancellationToken);

        Response.Headers.ETag = EntityTagCodec.Format(response.DraftVersion);
        return Ok(response);
    }

    /// <summary>Confirms the explicitly selected resolved draft skills.</summary>
    /// <remarks>
    /// If-Match must contain the current draft ETag. Existing job posting
    /// skills are preserved and only missing selected skills are added.
    /// </remarks>
    [HttpPost("parse-drafts/{draftId:guid}/confirm")]
    [ProducesResponseType(
        typeof(JobPostingParseDraftResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<JobPostingParseDraftResponse>> Confirm(
        Guid jobPostingId,
        Guid draftId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] ConfirmJobPostingParseDraftRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = EntityTagPreconditions.GetRequiredVersion(ifMatch);
        var response = await _draftService.ConfirmDraftAsync(
            jobPostingId,
            draftId,
            expectedVersion,
            request,
            cancellationToken);

        Response.Headers.ETag = EntityTagCodec.Format(response.DraftVersion);
        return Ok(response);
    }
}

using Api.Application.Analysis;
using Api.DTOs.Analysis;
using Api.DTOs.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/analyses")]
public sealed class ResumeAnalysesController : ControllerBase
{
    private readonly IResumeAnalysisService _analysisService;

    public ResumeAnalysesController(IResumeAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(ResumeAnalysisResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<ActionResult<ResumeAnalysisResponse>> Analyze(
        [FromBody] AnalyzeResumeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _analysisService.AnalyzeAsync(
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { analysisId = response.Id },
            response);
    }

    [HttpGet("{analysisId:guid}")]
    [ProducesResponseType(
        typeof(ResumeAnalysisResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeAnalysisResponse>> GetById(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        var response = await _analysisService.GetByIdAsync(
            analysisId,
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("/api/resumes/{resumeId:guid}/analyses")]
    [ProducesResponseType(
        typeof(PagedResponse<AnalysisListItemResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<AnalysisListItemResponse>>>
        GetHistory(
            Guid resumeId,
            CancellationToken cancellationToken,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
    {
        var response = await _analysisService.GetHistoryAsync(
            resumeId,
            page,
            pageSize,
            cancellationToken);

        return Ok(response);
    }
}

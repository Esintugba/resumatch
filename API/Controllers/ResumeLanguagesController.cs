using Api.Application.Languages;
using Api.DTOs.Language;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/{resumeId:guid}/languages")]
public sealed class ResumeLanguagesController : ControllerBase
{
    private readonly ILanguageService _languageService;

    public ResumeLanguagesController(ILanguageService languageService)
    {
        _languageService = languageService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LanguageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LanguageResponse>>> GetAll(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _languageService.GetLanguagesAsync(
            resumeId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(LanguageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LanguageResponse>> Create(
        Guid resumeId,
        [FromBody] CreateLanguageRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _languageService.CreateAsync(
            resumeId,
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { resumeId, languageId = response.Id },
            response);
    }

    [HttpGet("{languageId:guid}")]
    [ProducesResponseType(typeof(LanguageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LanguageResponse>> GetById(
        Guid resumeId,
        Guid languageId,
        CancellationToken cancellationToken)
    {
        var response = await _languageService.GetByIdAsync(
            resumeId,
            languageId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPut("{languageId:guid}")]
    [ProducesResponseType(typeof(LanguageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LanguageResponse>> Update(
        Guid resumeId,
        Guid languageId,
        [FromBody] UpdateLanguageRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _languageService.UpdateAsync(
            resumeId,
            languageId,
            request,
            cancellationToken);

        return Ok(response);
    }

    [HttpDelete("{languageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        Guid languageId,
        CancellationToken cancellationToken)
    {
        await _languageService.DeleteAsync(
            resumeId,
            languageId,
            cancellationToken);

        return NoContent();
    }
}

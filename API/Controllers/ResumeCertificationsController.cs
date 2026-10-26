using Api.Application.Certifications;
using Api.DTOs.Certification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/{resumeId:guid}/certifications")]
public sealed class ResumeCertificationsController : ControllerBase
{
    private readonly ICertificationService _certificationService;

    public ResumeCertificationsController(ICertificationService certificationService)
    {
        _certificationService = certificationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CertificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CertificationResponse>>> GetAll(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var response = await _certificationService.GetCertificationsAsync(
            resumeId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CertificationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CertificationResponse>> Create(
        Guid resumeId,
        [FromBody] CreateCertificationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _certificationService.CreateAsync(
            resumeId,
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { resumeId, certificationId = response.Id },
            response);
    }

    [HttpGet("{certificationId:guid}")]
    [ProducesResponseType(typeof(CertificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CertificationResponse>> GetById(
        Guid resumeId,
        Guid certificationId,
        CancellationToken cancellationToken)
    {
        var response = await _certificationService.GetByIdAsync(
            resumeId,
            certificationId,
            cancellationToken);

        return Ok(response);
    }

    [HttpPut("{certificationId:guid}")]
    [ProducesResponseType(typeof(CertificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CertificationResponse>> Update(
        Guid resumeId,
        Guid certificationId,
        [FromBody] UpdateCertificationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _certificationService.UpdateAsync(
            resumeId,
            certificationId,
            request,
            cancellationToken);

        return Ok(response);
    }

    [HttpDelete("{certificationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid resumeId,
        Guid certificationId,
        CancellationToken cancellationToken)
    {
        await _certificationService.DeleteAsync(
            resumeId,
            certificationId,
            cancellationToken);

        return NoContent();
    }
}

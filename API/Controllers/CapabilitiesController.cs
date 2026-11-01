using Api.Application.JobParsing;
using Api.DTOs.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/capabilities")]
public sealed class CapabilitiesController : ControllerBase
{
    private readonly IJobParsingAvailability _jobParsingAvailability;

    public CapabilitiesController(IJobParsingAvailability jobParsingAvailability)
    {
        _jobParsingAvailability = jobParsingAvailability ??
            throw new ArgumentNullException(nameof(jobParsingAvailability));
    }

    [HttpGet]
    [ProducesResponseType(typeof(CapabilitiesResponse), StatusCodes.Status200OK)]
    public ActionResult<CapabilitiesResponse> Get() =>
        Ok(new CapabilitiesResponse(_jobParsingAvailability.IsEnabled));
}

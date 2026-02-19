using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TransactionApi.Controllers;

[ApiController]
[Route("health")]
[Tags("Health")]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <summary>
    /// Verifica o estado de saúde da aplicação.
    /// </summary>
    /// <response code="200">Aplicação saudável.</response>
    /// <response code="503">Alguma dependência está indisponível.</response>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var report = await _healthCheckService.CheckHealthAsync();

        if (report.Status == HealthStatus.Healthy)
            return Ok(report.Status.ToString());

        return StatusCode(StatusCodes.Status503ServiceUnavailable, report.Status.ToString());
    }
}

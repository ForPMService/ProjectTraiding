using Microsoft.AspNetCore.Mvc;
using ProjectTraiding.Api.Health;

namespace ProjectTraiding.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    private readonly ConfigurationHealthChecker _configurationHealthChecker;

    public HealthController(ConfigurationHealthChecker configurationHealthChecker)
    {
        _configurationHealthChecker = configurationHealthChecker;
    }

    [HttpGet("/health/live")]
    public IActionResult Live()
    {
        return Ok(new { status = "alive" });
    }

    [HttpGet("/health/ready")]
    public IActionResult Ready()
    {
        var response = _configurationHealthChecker.Check();
        return Ok(response);
    }
}

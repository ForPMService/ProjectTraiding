using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Api.Health;
using ProjectTraiding.Contracts.Health;

namespace ProjectTraiding.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    private readonly ConfigurationHealthChecker _configurationHealthChecker;
    private readonly ILogger<HealthController> _logger;
    private readonly IServiceProvider _serviceProvider;

    public HealthController(
        ConfigurationHealthChecker configurationHealthChecker,
        ILogger<HealthController> logger,
        IServiceProvider serviceProvider)
    {
        _configurationHealthChecker = configurationHealthChecker;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    [HttpGet("/health/live")]
    public IActionResult Live()
    {
        return Ok(new { status = "alive" });
    }

    [HttpGet("/health/ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var configResponse = _configurationHealthChecker.Check();

        if (!string.Equals(configResponse.Status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, configResponse);
        }

        try
        {
            var infrastructureHealthChecker = _serviceProvider.GetRequiredService<InfrastructureHealthChecker>();
            var infraServices = await infrastructureHealthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);

            var combinedList = configResponse.Services.Concat(infraServices).ToList();

            var overall = ComputeOverallStatus(combinedList);

            var response = new HealthStatusResponse(overall, combinedList.AsReadOnly());

            if (string.Equals(overall, "ready", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(response);
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Infrastructure health check failed");

            var combinedList = configResponse.Services.ToList();
            combinedList.Add(new ServiceHealthItem("infrastructure", "unhealthy", "health check failed", null, "internal_error"));

            var response = new HealthStatusResponse("unhealthy", combinedList.AsReadOnly());
            return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
        }
    }

    private static string ComputeOverallStatus(System.Collections.Generic.IEnumerable<ServiceHealthItem> services)
    {
        var statuses = services
            .Where(s => !string.IsNullOrWhiteSpace(s.Status))
            .Select(s => s.Status!.Trim())
            .Where(s => s.Length > 0)
            .Select(s => s.ToLowerInvariant())
            .ToList();

        if (statuses.Any(s => string.Equals(s, "unhealthy", StringComparison.OrdinalIgnoreCase)))
        {
            return "unhealthy";
        }

        if (statuses.Any(s => string.Equals(s, "degraded", StringComparison.OrdinalIgnoreCase)))
        {
            return "degraded";
        }

        return "ready";
    }
}

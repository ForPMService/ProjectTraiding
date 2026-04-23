using ProjectTraiding.Infrastructure.Configuration;
using ProjectTraiding.Infrastructure.Health;
using ProjectTraiding.Shared.Configuration;
using ProjectTraiding.Api.Middleware;
using ProjectTraiding.Api.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProjectTraidingOptions(builder.Configuration);
builder.Services.AddProjectTraidingObservability();
builder.Services.AddInfrastructureHealth();
builder.Services.AddSingleton<ConfigurationHealthChecker>();
builder.Services.AddControllers();

// Minimal HttpContext accessor and correlation id accessor for observability
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICorrelationIdAccessor, HttpContextCorrelationIdAccessor>();

var app = builder.Build();

// Correlation id middleware: ensures X-Correlation-Id is present and available via HttpContext.Items
app.UseMiddleware<CorrelationIdMiddleware>();

// Request logging middleware: minimal request started/finished/failure events
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapControllers();

app.Run();

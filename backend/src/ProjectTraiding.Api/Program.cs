using ProjectTraiding.Infrastructure.Configuration;
using ProjectTraiding.Infrastructure.Health;
using ProjectTraiding.Shared.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProjectTraidingOptions(builder.Configuration);
builder.Services.AddInfrastructureHealth();
builder.Services.AddSingleton<ConfigurationHealthChecker>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();

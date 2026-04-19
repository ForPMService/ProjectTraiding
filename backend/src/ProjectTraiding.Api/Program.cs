using ProjectTraiding.Api.Configuration;
using ProjectTraiding.Api.Endpoints;
using ProjectTraiding.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProjectTraidingOptions(builder.Configuration);
builder.Services.AddSingleton<ConfigurationHealthChecker>();

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

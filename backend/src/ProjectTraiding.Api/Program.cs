using ProjectTraiding.Api.Configuration;
using ProjectTraiding.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProjectTraidingOptions(builder.Configuration);
builder.Services.AddSingleton<ConfigurationHealthChecker>();
builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();

app.Run();

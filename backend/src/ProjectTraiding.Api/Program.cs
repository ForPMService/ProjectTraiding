using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMoexClients(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapMoexEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapMoexDebugEndpoints();
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();

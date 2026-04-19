using System;
using ProjectTraiding.Api.Configuration;
using ProjectTraiding.Api.Health;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ProjectTraiding.Shared.Configuration;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProjectTraidingOptions(builder.Configuration);

// Named HttpClients for infrastructure health checks
builder.Services.AddHttpClient("clickhouse-health").ConfigureHttpClient((sp, client) =>
{
	var infra = sp.GetRequiredService<IOptions<InfrastructureHealthOptions>>().Value;
	var timeoutMs = infra is not null && infra.TimeoutMs > 0 ? infra.TimeoutMs : 2000;
	client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
});

builder.Services.AddHttpClient("minio-health").ConfigureHttpClient((sp, client) =>
{
	var infra = sp.GetRequiredService<IOptions<InfrastructureHealthOptions>>().Value;
	var timeoutMs = infra is not null && infra.TimeoutMs > 0 ? infra.TimeoutMs : 2000;
	client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
});

// Register NpgsqlDataSource as a singleton (created from PostgresOptions)
builder.Services.AddSingleton(sp =>
{
	var pg = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
	var csb = new NpgsqlConnectionStringBuilder
	{
		Host = pg.Host,
		Port = pg.Port,
		Database = pg.Database,
		Username = pg.User,
		Password = pg.Password,
		Pooling = true
	};

	return NpgsqlDataSource.Create(csb.ConnectionString);
});

// Redis provider and infrastructure health checker
builder.Services.AddSingleton<RedisHealthConnectionProvider>();
builder.Services.AddSingleton<InfrastructureHealthChecker>();

builder.Services.AddSingleton<ConfigurationHealthChecker>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();

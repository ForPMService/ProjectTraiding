using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Infrastructure.Configuration;
using ProjectTraiding.Shared.Configuration;
using ProjectTraiding.Worker;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddProjectTraidingOptions(context.Configuration);
        services.AddProjectTraidingObservability(context.Configuration);
        services.AddHostedService<NoopWorker>();
    })
    .Build();

await host.RunAsync();

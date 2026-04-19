using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectTraiding.Worker.Configuration;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddProjectTraidingOptions(context.Configuration);
        services.AddHostedService<NoopWorker>();
    })
    .Build();

await host.RunAsync();

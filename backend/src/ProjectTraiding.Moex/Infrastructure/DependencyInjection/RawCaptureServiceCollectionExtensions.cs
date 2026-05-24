using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Options;

namespace ProjectTraiding.Moex.Infrastructure.DependencyInjection;

public static class RawCaptureServiceCollectionExtensions
{
    public static IServiceCollection AddRawCapture(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RawCaptureOptions>()
            .Bind(configuration.GetSection("RawCapture"));

        services.AddSingleton<IAmazonS3>(sp =>
        {
            RawCaptureOptions options = sp.GetRequiredService<IOptions<RawCaptureOptions>>().Value;

            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = string.IsNullOrEmpty(options.Endpoint)
                    ? "http://localhost:3900"
                    : options.Endpoint,
                AuthenticationRegion = options.Region,
                ForcePathStyle = true
            };

            BasicAWSCredentials credentials = new BasicAWSCredentials(
                string.IsNullOrEmpty(options.AccessKey) ? "not-configured" : options.AccessKey,
                string.IsNullOrEmpty(options.SecretKey) ? "not-configured" : options.SecretKey);

            return new AmazonS3Client(credentials, config);
        });

        return services;
    }
}

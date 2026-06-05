using Npgsql;

namespace ProjectTraiding.Api.Infrastructure
{
    public static class PostgreService
    {
        public static IServiceCollection AddPostgre(this IServiceCollection services, IConfiguration configuration)
        {
            string connectionString = configuration.GetConnectionString("ProjectTraidingDb");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string 'ProjectTraidingDb' not found.");
            }
            NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
            services.AddSingleton(dataSource);
            return services;
        }
    }
}

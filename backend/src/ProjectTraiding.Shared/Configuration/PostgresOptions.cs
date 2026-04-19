namespace ProjectTraiding.Shared.Configuration;

public sealed class PostgresOptions
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 0;
    public string Database { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

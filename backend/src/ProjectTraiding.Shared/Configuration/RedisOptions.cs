namespace ProjectTraiding.Shared.Configuration;

public sealed class RedisOptions
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 0;
}

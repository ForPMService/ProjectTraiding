namespace ProjectTraiding.Shared.Configuration;

public sealed class RedisOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 0;
}

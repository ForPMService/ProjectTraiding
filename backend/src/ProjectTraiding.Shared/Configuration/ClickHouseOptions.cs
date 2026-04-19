namespace ProjectTraiding.Shared.Configuration;

public sealed class ClickHouseOptions
{
    public string Host { get; init; } = string.Empty;
    public int HttpPort { get; init; } = 0;
    public int NativePort { get; init; } = 0;
    public string Database { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

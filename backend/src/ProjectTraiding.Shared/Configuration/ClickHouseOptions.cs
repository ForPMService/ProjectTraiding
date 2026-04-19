namespace ProjectTraiding.Shared.Configuration;

public sealed class ClickHouseOptions
{
    public string Host { get; set; } = string.Empty;
    public int HttpPort { get; set; } = 0;
    public int NativePort { get; set; } = 0;
    public string Database { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

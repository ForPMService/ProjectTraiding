namespace ProjectTraiding.Telegram;

public sealed class TelegramOptions
{
    public bool Enabled { get; set; }
    public int ApiId { get; set; }
    public string ApiHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string SessionPath { get; set; } = string.Empty;
    public string UpdateStatePath { get; set; } = string.Empty;
    public string[] Channels { get; set; } = [];
    public string? OutboundProxyUrl { get; set; }
}

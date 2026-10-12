using Microsoft.Extensions.Configuration;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>Independent owner bot configuration; disabled bots need no credentials.</summary>
public sealed class EuropeTelegramBotOptions
{
    public bool Enabled { get; init; }
    public string BotToken { get; init; } = string.Empty;
    public long OwnerUserId { get; init; }
    public int PollTimeoutSeconds { get; init; } = 20;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxConsecutiveFailures { get; init; } = 3;

    internal static EuropeTelegramBotOptions Load(IConfiguration configuration)
    {
        string? enabledText = configuration["EuropeTelegramBot:Enabled"];
        if (enabledText is null || bool.TryParse(enabledText, out bool enabled) && !enabled)
            return new EuropeTelegramBotOptions();
        EuropeTelegramBotOptions options;
        try
        {
            options = configuration.GetSection("EuropeTelegramBot").Get<EuropeTelegramBotOptions>() ?? new();
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException("InvalidConfiguration: owner bot binding failed.");
        }
        int separator = options.BotToken.IndexOf(':');
        if (!options.Enabled || separator <= 0 || separator == options.BotToken.Length - 1
            || !long.TryParse(options.BotToken.AsSpan(0, separator), out long botId) || botId <= 0
            || options.OwnerUserId <= 0 || options.PollTimeoutSeconds is <= 0 or > 50
            || !TelegramOptions.ValidTimeout(options.RequestTimeout)
            || options.RequestTimeout <= TimeSpan.FromSeconds(options.PollTimeoutSeconds)
            || !TelegramOptions.ValidTimeout(options.RetryDelay) || options.MaxConsecutiveFailures <= 0)
            throw new InvalidOperationException("InvalidConfiguration: owner bot parameters are invalid.");
        return options;
    }
}

using Microsoft.Extensions.Configuration;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>Fixed startup sources and destination bot; credentials are supplied by the environment.</summary>
public sealed class TelegramOptions
{
    public bool Enabled { get; init; }
    public int ApiId { get; init; }
    public string ApiHash { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string SessionPath { get; init; } = string.Empty;
    public string UpdateStatePath { get; init; } = string.Empty;
    public string[] Channels { get; init; } = [];
    public string ForwardBotUsername { get; init; } = string.Empty;
    public int ForwardQueueCapacity { get; init; }
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal static TelegramOptions Load(IConfiguration configuration)
    {
        string? enabledText = configuration["Telegram:Enabled"];
        if (enabledText is null || bool.TryParse(enabledText, out bool enabled) && !enabled)
            return new TelegramOptions();

        TelegramOptions options;
        try
        {
            options = configuration.GetSection("Telegram").Get<TelegramOptions>() ?? new();
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException("Telegram configuration binding failed.");
        }
        if (!options.Enabled || options.ApiId <= 0 || options.ApiHash.Length != 32
            || string.IsNullOrWhiteSpace(options.PhoneNumber) || options.Channels.Length == 0
            || options.ForwardQueueCapacity <= 0 || !ValidTimeout(options.OperationTimeout)
            || !ValidTimeout(options.ShutdownTimeout)
            || !ValidStoragePath(options.SessionPath) || !ValidStoragePath(options.UpdateStatePath)
            || string.Equals(Path.GetFullPath(options.SessionPath), Path.GetFullPath(options.UpdateStatePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Telegram configuration is invalid.");

        foreach (char character in options.ApiHash)
            if (!Uri.IsHexDigit(character))
                throw new InvalidOperationException("Telegram credentials are invalid.");

        HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
        foreach (string username in options.Channels)
            if (!sources.Add(NormalizeUsername(username)))
                throw new InvalidOperationException("Telegram source channels must be distinct.");
        NormalizeUsername(options.ForwardBotUsername);
        return options;
    }

    internal static string NormalizeUsername(string username)
    {
        string candidate = username.Trim().TrimStart('@');
        if (candidate.Length is < 5 or > 32 || !char.IsAsciiLetter(candidate[0]))
            throw new InvalidOperationException("Telegram public username is invalid.");
        foreach (char character in candidate)
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
                throw new InvalidOperationException("Telegram public username is invalid.");
        return candidate;
    }

    private static bool ValidTimeout(TimeSpan timeout) => timeout > TimeSpan.Zero && timeout.TotalMilliseconds <= int.MaxValue;

    private static bool ValidStoragePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || string.IsNullOrEmpty(Path.GetFileName(path))) return false;
        string fullPath = Path.GetFullPath(path);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (fullPath.StartsWith(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) + Path.DirectorySeparatorChar, comparison))
            return false;
        DirectoryInfo? source = new(Directory.GetCurrentDirectory());
        while (source is not null)
        {
            if (Directory.Exists(Path.Combine(source.FullName, ".git")) || File.Exists(Path.Combine(source.FullName, ".git")))
            {
                if (fullPath.StartsWith(source.FullName + Path.DirectorySeparatorChar, comparison)) return false;
                break;
            }
            source = source.Parent;
        }
        DirectoryInfo? parent = new(Path.GetDirectoryName(fullPath)!);
        while (parent is not null)
        {
            if (parent.Name is ".git" or "bin" or "obj" || parent.LinkTarget is not null) return false;
            parent = parent.Parent;
        }
        return !File.Exists(fullPath) || new FileInfo(fullPath).LinkTarget is null;
    }
}

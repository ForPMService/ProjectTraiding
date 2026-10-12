using Microsoft.Extensions.Configuration;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>Operator configuration. Secrets are supplied by the environment.</summary>
public sealed class TelegramOptions
{
    public bool Enabled { get; init; }
    public bool AutoStart { get; init; }
    public int ApiId { get; init; }
    public string ApiHash { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string SessionPath { get; init; } = string.Empty;
    public string UpdateStatePath { get; init; } = string.Empty;
    public string ChannelsPath { get; init; } = string.Empty;
    public int QueueCapacity { get; init; }
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ForwardTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan CommandWaitTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public string ControlPeerKind { get; init; } = string.Empty;
    public long ControlPeerId { get; init; }
    public string? OutboundProxyUrl { get; init; }

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
            // Binding exceptions can contain the original secret value.
            throw new InvalidOperationException("InvalidConfiguration: Telegram binding failed.");
        }
        if (!options.Enabled || options.ApiId <= 0 || options.ApiHash.Length != 32
            || string.IsNullOrWhiteSpace(options.PhoneNumber) || options.QueueCapacity <= 0
            || !string.IsNullOrEmpty(options.OutboundProxyUrl)
            || options.ControlPeerId <= 0 || options.ControlPeerKind is not ("User" or "Chat" or "Channel")
            || !ValidTimeout(options.OperationTimeout) || !ValidTimeout(options.ForwardTimeout)
            || !ValidTimeout(options.DrainTimeout) || !ValidTimeout(options.CloseTimeout)
            || !ValidTimeout(options.CommandWaitTimeout) || options.ForwardTimeout > options.DrainTimeout)
            throw new InvalidOperationException("InvalidConfiguration: Telegram parameters are invalid.");
        foreach (char character in options.ApiHash)
            if (!Uri.IsHexDigit(character))
                throw new InvalidOperationException("InvalidConfiguration: Telegram credentials are invalid.");
        string[] paths = [options.SessionPath, options.UpdateStatePath, options.ChannelsPath];
        HashSet<string> distinct = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!ValidStoragePath(path) || !distinct.Add(Path.GetFullPath(path)))
                throw new InvalidOperationException("InvalidConfiguration: state paths must be distinct and outside the source and publication trees.");
        }
        return options;
    }

    internal static bool ValidTimeout(TimeSpan timeout) => timeout > TimeSpan.Zero && timeout.TotalMilliseconds <= int.MaxValue;

    private static bool ValidStoragePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || string.IsNullOrEmpty(Path.GetFileName(path))) return false;
        string fullPath = Path.GetFullPath(path);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string basePath = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, comparison)) return false;
        // A source checkout is found by .git, without pinning SDKs or changing the Russian project.
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

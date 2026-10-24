using Microsoft.Extensions.Configuration;

namespace ProjectTraiding.Europe.News;

/// <summary>Проверяет операторские пределы до создания очереди и внешнего клиента.</summary>
public sealed class NewsAgentOptions
{
    public bool Enabled { get; init; }
    public int QueueCapacity { get; init; }
    public int Workers { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Выключенный контур не требует ключа и не привязывает неиспользуемые настройки.</summary>
    public static NewsAgentOptions Load(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("News:Enabled")) return new();
        NewsAgentOptions options = new()
        {
            Enabled = true,
            QueueCapacity = configuration.GetValue<int>("News:QueueCapacity"),
            Workers = configuration.GetValue<int>("News:Workers"),
            RequestTimeout = configuration.GetValue("News:RequestTimeout", TimeSpan.FromSeconds(30))
        };
        if (options.QueueCapacity is < 1 or > 65536 || options.Workers is < 1 or > 64
            || options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(10))
            throw new InvalidOperationException("Некорректные пределы News.");

        LlmProfileOptions profile = LlmProfileOptions.Load(configuration);
        _ = LlmProfileOptions.GetApiKey(configuration, profile);
        return options;
    }
}

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
        NewsAgentOptions options = configuration.GetSection("News").Get<NewsAgentOptions>()!;
        if (options.QueueCapacity is < 1 or > 65536 || options.Workers is < 1 or > 64
            || options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(10)
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
            throw new InvalidOperationException("Некорректные пределы News или отсутствует OPENAI_API_KEY в среде.");
        return options;
    }
}

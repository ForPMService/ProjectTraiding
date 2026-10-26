using Microsoft.Extensions.Configuration;

namespace ProjectTraiding.Europe.Grpc;

/// <summary>Конфигурация двустороннего gRPC-обмена новостями.</summary>
public sealed record NewsGrpcOptions
{
    /// <summary>Включает gRPC-транспорт независимо от анализа News.</summary>
    public bool Enabled { get; init; }
    /// <summary>Адрес российского gRPC-сервера.</summary>
    public string Address { get; init; } = string.Empty;
    /// <summary>Задержка между повторными подписками.</summary>
    public TimeSpan ReconnectDelay { get; init; }
    /// <summary>Максимальное ожидание первого сообщения каждого потока.</summary>
    public TimeSpan FirstSnapshotTimeout { get; init; }
    /// <summary>Срок одной попытки отправки результата.</summary>
    public TimeSpan RequestTimeout { get; init; }
    /// <summary>Число ожидающих результатов в ограниченном канале.</summary>
    public int ResultQueueCapacity { get; init; }
    /// <summary>Максимальное число попыток отправки результата.</summary>
    public int MaxSendAttempts { get; init; }
    /// <summary>Максимальный размер входящего gRPC-сообщения.</summary>
    public int MaxReceiveMessageSize { get; init; }
    /// <summary>Максимальный размер исходящего gRPC-сообщения.</summary>
    public int MaxSendMessageSize { get; init; }

    /// <summary>Загружает и проверяет настройки активного транспорта.</summary>
    public static NewsGrpcOptions Load(IConfiguration configuration, bool probeMode = false, bool validateSending = false)
    {
        NewsGrpcOptions options = new()
        {
            Enabled = configuration.GetValue<bool>("NewsGrpc:Enabled"),
            Address = configuration["NewsGrpc:Address"] ?? string.Empty,
            ReconnectDelay = configuration.GetValue("NewsGrpc:ReconnectDelay", TimeSpan.Zero),
            FirstSnapshotTimeout = configuration.GetValue("NewsGrpc:FirstSnapshotTimeout", TimeSpan.Zero),
            RequestTimeout = configuration.GetValue("NewsGrpc:RequestTimeout", TimeSpan.Zero),
            ResultQueueCapacity = configuration.GetValue<int>("NewsGrpc:ResultQueueCapacity"),
            MaxSendAttempts = configuration.GetValue<int>("NewsGrpc:MaxSendAttempts"),
            MaxReceiveMessageSize = configuration.GetValue<int>("NewsGrpc:MaxReceiveMessageSize"),
            MaxSendMessageSize = configuration.GetValue<int>("NewsGrpc:MaxSendMessageSize")
        };
        if (!options.Enabled) return options;

        if (!Uri.TryCreate(options.Address, UriKind.Absolute, out Uri? address)
            || !string.IsNullOrEmpty(address.UserInfo) || address.AbsolutePath != "/"
            || address.Query.Length != 0 || address.Fragment.Length != 0
            || address.Scheme != Uri.UriSchemeHttps
                && !(probeMode && address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
            throw new InvalidOperationException("NewsGrpc:Address must be an HTTPS server URI; HTTP is allowed only for the loopback probe.");
        if (options.ReconnectDelay <= TimeSpan.Zero || options.ReconnectDelay.TotalMilliseconds > int.MaxValue
            || options.FirstSnapshotTimeout <= TimeSpan.Zero || options.FirstSnapshotTimeout.TotalMilliseconds > int.MaxValue
            || options.MaxReceiveMessageSize <= 0)
            throw new InvalidOperationException("Enabled NewsGrpc receive settings must be positive and finite.");
        if (validateSending && (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout.TotalMilliseconds > int.MaxValue
            || options.ResultQueueCapacity <= 0 || options.MaxSendAttempts <= 0 || options.MaxSendMessageSize <= 0))
            throw new InvalidOperationException("Enabled NewsGrpc send settings must be positive and finite.");
        return options;
    }
}

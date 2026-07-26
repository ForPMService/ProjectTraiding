namespace ProjectTraiding.Diagnostics.Options;

/// <summary>
/// Проверка настроек пробника потокового соединения при запуске среды разработки.
/// Учётные данные здесь не проверяются: без них приложение должно запускаться, пока
/// оператор не обращается к пробнику; наличие проверяется непосредственно перед подключением.
/// </summary>
public static class WebSocketProbeOptionsValidator
{
    public static void Validate(WebSocketProbeOptions options)
    {
        // Схема — только wss. По этому каналу передаётся пароль учётной записи, и
        // незашифрованный ws здесь недопустим. Понадобится локальный сервер по ws —
        // это будет отдельный явный режим, а не допустимое значение настройки.
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out Uri? webSocketUri)
            || !string.Equals(webSocketUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Diagnostics:WebSocketProbe:Url должен быть абсолютным адресом со схемой wss.");
        }

        if (string.IsNullOrWhiteSpace(options.Domain))
            throw new InvalidOperationException(
                "Diagnostics:WebSocketProbe:Domain не может быть пустым.");

        // Не меньше секунды: клиент снизу усекает длительность до одной секунды, и
        // меньший предел противоречил бы сам себе.
        if (options.MaxDuration < TimeSpan.FromSeconds(1))
            throw new InvalidOperationException(
                "Diagnostics:WebSocketProbe:MaxDuration должен быть не меньше одной секунды.");

        if (options.MaxFrames <= 0)
            throw new InvalidOperationException(
                "Diagnostics:WebSocketProbe:MaxFrames должен быть положительным.");

        if (options.MaxCapturedBytes <= 0)
            throw new InvalidOperationException(
                "Diagnostics:WebSocketProbe:MaxCapturedBytes должен быть положительным.");
    }
}

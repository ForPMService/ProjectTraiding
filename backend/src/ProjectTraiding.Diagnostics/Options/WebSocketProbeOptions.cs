namespace ProjectTraiding.Diagnostics.Options;

/// <summary>
/// Настройки пробника потокового соединения биржи. Перенесены из MoexOptions вместе со всем
/// слоем потокового соединения: боевой путь текущих данных построен на опросе через REST.
/// Аутентификация не ключом-предъявителем, а учётной записью: заголовки кадра CONNECT —
/// domain, login, passcode.
/// </summary>
public class WebSocketProbeOptions
{
    /// <summary>Точка подключения. Только защищённая схема: по каналу идёт пароль.</summary>
    public string Url { get; set; } = "wss://iss.moex.com/infocx/v3/websocket";

    /// <summary>Область: passport для подписчиков, DEMO для гостевого режима.</summary>
    public string Domain { get; set; } = "passport";

    /// <summary>Логин учётной записи. Через пользовательские секреты. В журнал не пишется.</summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>Пароль учётной записи. Через пользовательские секреты. В журнал не пишется НИКОГДА.</summary>
    public string Passcode { get; set; } = string.Empty;

    /// <summary>Предельная длительность сбора кадров.</summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Предельное число собираемых кадров. Защита памяти.</summary>
    public int MaxFrames { get; set; } = 500;

    /// <summary>Предельный суммарный объём собранных кадров в байтах. Защита памяти.</summary>
    public int MaxCapturedBytes { get; set; } = 5 * 1024 * 1024;
}

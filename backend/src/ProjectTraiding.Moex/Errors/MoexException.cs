namespace ProjectTraiding.Moex.Errors;

/// <summary>
/// Корень типизированных ошибок контура MOEX. Представляет отказ взаимодействия
/// с источником независимо от того, состоялся ли HTTP-обмен: сюда одинаково
/// относятся ответ с ошибочным статусом, несовпадение схемы ответа и отказ
/// локального ограничителя частоты до отправки запроса.
///
/// Свойства, применимые только к состоявшемуся HTTP-обмену, живут ниже,
/// в <see cref="ProjectTraiding.Moex.Clients.Errors.MoexHttpException"/>,
/// и в корень не поднимаются.
/// </summary>
public abstract class MoexException : Exception
{
    /// <summary>
    /// Стабильная категория ошибки для структурированного журналирования
    /// и телеметрии. Значения определены в
    /// <see cref="ProjectTraiding.Moex.Infrastructure.Telemetry.MoexErrorTypes"/>.
    /// </summary>
    public abstract string ErrorCategory { get; }

    protected MoexException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

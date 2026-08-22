using ProjectTraiding.Moex.Errors;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Метрики контура MOEX. Каталог: «Наблюдаемость — план закрытия фазы 2 v0.3», раздел 3.
///
/// Используются в местах действия:
///   MoexHttpLoggingHandler — HTTP-запросы к MOEX.
///   MoexRateLimitHandler   — rate limiter.
///   MOEX-клиенты           — пагинация (страницы, строки).
///
/// Атрибуты метрик: MoexTelemetryAttributes.Source, .DataKind, .StatusCode, .ErrorType.
/// Не полный endpoint — правила кардинальности (план, раздел 2.7).
/// </summary>
public static class MoexMetrics
{
    private static readonly Meter Meter = new(MoexTelemetry.MeterName);

    // ══════════════════════════════════════════════
    // HTTP-запросы к MOEX
    // ══════════════════════════════════════════════

    /// <summary>Количество HTTP-запросов к MOEX.</summary>
    public static readonly Counter<long> HttpRequests =
        Meter.CreateCounter<long>("moex.http.requests", description: "MOEX HTTP requests total");

    /// <summary>Длительность HTTP-запроса (мс).</summary>
    public static readonly Histogram<double> HttpRequestDuration =
        Meter.CreateHistogram<double>("moex.http.request.duration", unit: "ms", description: "MOEX HTTP request duration");

    /// <summary>Количество ошибок HTTP (5xx, timeout, transport).</summary>
    public static readonly Counter<long> HttpErrors =
        Meter.CreateCounter<long>("moex.http.errors", description: "MOEX HTTP errors total");

    /// <summary>Количество повторных попыток (Polly retry).</summary>
    public static readonly Counter<long> HttpRetries =
        Meter.CreateCounter<long>("moex.http.retries", description: "MOEX HTTP retries total");

    // ══════════════════════════════════════════════
    // Rate limiter
    // ══════════════════════════════════════════════

    /// <summary>Время ожидания permit (мс).</summary>
    public static readonly Histogram<double> RateLimitWaitDuration =
        Meter.CreateHistogram<double>("moex.ratelimit.wait.duration", unit: "ms", description: "MOEX rate limit wait duration");

    /// <summary>Количество запросов, ждавших дольше порога.</summary>
    public static readonly Counter<long> RateLimitQueued =
        Meter.CreateCounter<long>("moex.ratelimit.queued", description: "MOEX rate limit queued requests");

    /// <summary>
    /// Количество отказов ограничителя: запрос не получил разрешение и в сеть не ушёл.
    /// Причина различает мгновенный отказ переполненной очереди и истечение ожидания.
    /// </summary>
    public static readonly Counter<long> RateLimitRejected =
        Meter.CreateCounter<long>(
            "moex.ratelimit.rejected",
            description: "MOEX rate limit rejections total");

    // ══════════════════════════════════════════════
    // Получение данных
    // ══════════════════════════════════════════════

    /// <summary>
    /// Количество строк, полученных от источника и успешно разобранных.
    /// Сравнивается с числом успешно вставленных строк того же разреза.
    /// </summary>
    public static readonly Counter<long> RowsReceived =
        Meter.CreateCounter<long>(
            "moex.rows.received",
            description: "MOEX rows received and parsed total");

    /// <summary>
    /// Количество полученных страниц исторической пагинации.
    /// Одностраничные методы страницами не считаются: у них нет пагинации,
    /// и счёт таких вызовов дублировал бы счётчик операций.
    /// </summary>
    public static readonly Counter<long> PagesReceived =
        Meter.CreateCounter<long>(
            "moex.pages.received",
            description: "MOEX history pages received total");

    // ══════════════════════════════════════════════
    // Текущий приём
    // ══════════════════════════════════════════════

    /// <summary>
    /// Количество завершённых опросов одного инструмента текущего приёма.
    /// Единица шире операции источника: опрос включает не только обращение к бирже,
    /// но и обязательную постоянную запись с продвижением постоянного состояния.
    /// Успешный пустой ответ — тоже завершённый опрос.
    /// </summary>
    public static readonly Counter<long> RealtimePollCompleted =
        Meter.CreateCounter<long>(
            "moex.realtime.poll.completed",
            description: "MOEX realtime instrument polls completed total");

    /// <summary>
    /// Записывает исход полного опроса текущего инструмента. Метка источника не
    /// записывается: у приёма реального времени источник ровно один, и метка была бы
    /// постоянной величиной, добавляющей ряд без различающего смысла.
    /// </summary>
    public static void RecordRealtimePoll(in MoexOperationTags tags, string outcome)
    {
        RealtimePollCompleted.Add(
            1,
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, tags.DataKind),
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, tags.Market),
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.Outcome, outcome));
    }

    // ══════════════════════════════════════════════
    // Свежесть и здоровье текущего приёма
    // ══════════════════════════════════════════════

    /// <summary>
    /// Возраст последнего успешного опроса разреза, в секундах. Отвечает
    /// на вопрос «работает ли приёмник», а не «свежи ли данные».
    /// </summary>
    public static readonly ObservableGauge<double> RealtimeLastPollAge =
        Meter.CreateObservableGauge(
            "moex.realtime.last_poll_age",
            ObserveLastPollAge,
            unit: "s",
            description: "Age of the last successful realtime poll");

    /// <summary>
    /// Возраст последних записанных рыночных данных разреза, в секундах.
    /// Пустой успешный опрос это значение не омолаживает.
    /// </summary>
    public static readonly ObservableGauge<double> RealtimeDataAge =
        Meter.CreateObservableGauge(
            "moex.realtime.data_age",
            ObserveDataAge,
            unit: "s",
            description: "Age of the last stored realtime market data");

    /// <summary>Число инструментов разреза, по которым опрос давно не проходил.</summary>
    public static readonly ObservableGauge<long> RealtimeStaleInstruments =
        Meter.CreateObservableGauge(
            "moex.realtime.stale_instruments",
            ObserveStaleInstruments,
            description: "Instruments without a recent successful poll");

    /// <summary>Число инструментов разреза в активном приёме.</summary>
    public static readonly ObservableGauge<long> RealtimeActiveInstruments =
        Meter.CreateObservableGauge(
            "moex.realtime.active_instruments",
            ObserveActiveInstruments,
            description: "Instruments currently polled");

    /// <summary>
    /// Отметка времени последнего наблюдения в живом процессе, в секундах эпохи.
    /// Существует, чтобы отличить остановленное приложение от приложения,
    /// которое работает, но ничего не принимает.
    ///
    /// Отдельной фоновой службы не требует: значение вычисляется в момент опроса
    /// обработчика, а сам факт вызова обработчика уже означает, что процесс жив.
    /// </summary>
    public static readonly ObservableGauge<long> AppHeartbeatTime =
        Meter.CreateObservableGauge(
            "projecttraiding.app.heartbeat.time",
            ObserveHeartbeatTime,
            unit: "s",
            description: "Unix time of the last observation inside the live process");

    /// <summary>
    /// Гарантирует создание инструментов измерителя.
    ///
    /// Тело метода не пустое намеренно. У типа нет явного статического конструктора,
    /// поэтому он допускает раннюю инициализацию: среда обязана создать статические
    /// поля перед первым обращением к полю, но не перед вызовом статического метода.
    /// Пустой метод инициализацию не гарантирует, и при простое приложения инструменты
    /// не создались бы вовсе — сигнал присутствия отсутствовал бы именно тогда,
    /// когда он нужнее всего. Обращение к полю такую гарантию даёт.
    /// </summary>
    public static void EnsureInitialized()
    {
        GC.KeepAlive(AppHeartbeatTime);
    }

    private static IEnumerable<Measurement<double>> ObserveLastPollAge()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot> pair
                 in RealtimeTelemetryState.Enumerate())
        {
            DateTimeOffset? last = pair.Value.LastSuccessfulPollTime;
            if (last is null)
                continue;

            yield return new Measurement<double>(
                (now - last.Value).TotalSeconds,
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, pair.Key.DataKind),
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, pair.Key.Market));
        }
    }

    private static IEnumerable<Measurement<double>> ObserveDataAge()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot> pair
                 in RealtimeTelemetryState.Enumerate())
        {
            DateTimeOffset? last = pair.Value.LastConfirmedMarketTime;
            if (last is null)
                continue;

            yield return new Measurement<double>(
                (now - last.Value).TotalSeconds,
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, pair.Key.DataKind),
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, pair.Key.Market));
        }
    }

    private static IEnumerable<Measurement<long>> ObserveStaleInstruments()
    {
        foreach (KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot> pair
                 in RealtimeTelemetryState.Enumerate())
        {
            yield return new Measurement<long>(
                pair.Value.StaleInstruments,
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, pair.Key.DataKind),
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, pair.Key.Market));
        }
    }

    private static IEnumerable<Measurement<long>> ObserveActiveInstruments()
    {
        foreach (KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot> pair
                 in RealtimeTelemetryState.Enumerate())
        {
            yield return new Measurement<long>(
                pair.Value.ActiveInstruments,
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, pair.Key.DataKind),
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, pair.Key.Market));
        }
    }

    private static long ObserveHeartbeatTime()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    // ══════════════════════════════════════════════
    // Производственные операции источника
    // ══════════════════════════════════════════════

    /// <summary>
    /// Длительность одной производственной операции источника, в секундах.
    /// Границы заданы явно: умолчание библиотеки рассчитано на миллисекунды
    /// и для секундной шкалы даёт непригодное разрешение.
    ///
    /// Верхняя граница доведена до пяти минут — это полный бюджет одного запроса
    /// вместе со всеми повторами и ожиданиями между ними. Более низкий потолок
    /// сваливал бы в переполнение именно те наблюдения, ради которых метрику
    /// и смотрят: длительные операции в период повторов и деградации источника.
    /// </summary>
    public static readonly Histogram<double> OperationDuration =
        Meter.CreateHistogram<double>(
            "moex.operation.duration",
            unit: "s",
            description: "MOEX source operation duration",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = new double[]
                {
                    0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 180, 300
                }
            });

    /// <summary>Количество завершённых производственных операций источника.</summary>
    public static readonly Counter<long> OperationsCompleted =
        Meter.CreateCounter<long>(
            "moex.operations.completed",
            description: "MOEX source operations completed total");

    // ══════════════════════════════════════════════
    // Вставки в ClickHouse
    // ══════════════════════════════════════════════

    /// <summary>Количество выполненных операций вставки. Одна операция — один фактический INSERT.</summary>
    public static readonly Counter<long> StorageInsertOperations =
        Meter.CreateCounter<long>(
            "storage.insert.operations",
            description: "ClickHouse insert operations total");

    /// <summary>
    /// Количество вставленных строк. При успехе записывается значение, подтверждённое
    /// драйвером, а не размер отправленной пачки: расхождение между ними и есть то,
    /// ради чего метрика существует.
    /// </summary>
    public static readonly Counter<long> StorageInsertRows =
        Meter.CreateCounter<long>(
            "storage.insert.rows",
            description: "ClickHouse inserted rows total");

    /// <summary>
    /// Длительность одной вставки, в секундах. Собственного бюджета времени у вставки
    /// нет: клиент хранилища создаётся из строки подключения без тайм-аута, и длительность
    /// ограничена лишь токеном отмены вызывающей стороны. Поэтому верхняя граница взята
    /// с запасом до пяти минут — иначе при деградации хранилища все наблюдения свалились бы
    /// в переполнение, и процентиль потерял бы разрешение именно в аварийный период.
    /// </summary>
    public static readonly Histogram<double> StorageInsertDuration =
        Meter.CreateHistogram<double>(
            "storage.insert.duration",
            unit: "s",
            description: "ClickHouse insert duration",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = new double[]
                {
                    0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 180, 300
                }
            });

    /// <summary>
    /// Записывает успешный исход операции. Пустой, но корректно разобранный ответ —
    /// успех: отсутствие данных за период является нормальным состоянием источника.
    /// </summary>
    public static void RecordOperationSuccess(in MoexOperationTags tags, double seconds)
    {
        RecordOperation(in tags, MoexOutcomes.Success, null, seconds);
    }

    /// <summary>
    /// Записывает отмену вызывающей стороны. Категория ошибки не добавляется:
    /// отмена не является отказом источника.
    /// </summary>
    public static void RecordOperationCancelled(in MoexOperationTags tags, double seconds)
    {
        RecordOperation(in tags, MoexOutcomes.Cancelled, null, seconds);
    }

    /// <summary>
    /// Записывает отказ операции, определяя категорию по типу исключения.
    /// Классификация выполняется здесь, а не в вызывающем коде, чтобы одно
    /// исключение везде получало одну и ту же категорию.
    /// </summary>
    public static void RecordOperationError(
        in MoexOperationTags tags,
        Exception exception,
        double seconds)
    {
        RecordOperation(in tags, MoexOutcomes.Error, ClassifyError(exception), seconds);
    }

    /// <summary>
    /// Записывает отказ по истечению собственного бюджета ожидания. Отдельный метод
    /// нужен потому, что такое истечение приходит как отмена связанного токена и по
    /// типу исключения неотличимо от остановки хоста: различие известно только
    /// вызывающему коду, который и владеет обоими токенами.
    /// </summary>
    public static void RecordOperationTimeout(in MoexOperationTags tags, double seconds)
    {
        RecordOperation(in tags, MoexOutcomes.Error, MoexErrorTypes.Timeout, seconds);
    }

    private static void RecordOperation(
        in MoexOperationTags tags,
        string outcome,
        string? errorType,
        double seconds)
    {
        TagList tagList = new TagList
        {
            { MoexTelemetryAttributes.Source, tags.Source },
            { MoexTelemetryAttributes.Operation, tags.Operation },
            { MoexTelemetryAttributes.DataKind, tags.DataKind },
            { MoexTelemetryAttributes.Market, tags.Market },
            { MoexTelemetryAttributes.Outcome, outcome },
        };

        // Категория ошибки добавляется только при отказе. Пустая строка вместо
        // отсутствующей метки создала бы отдельный ряд с бессмысленным значением.
        if (errorType is not null)
            tagList.Add(MoexTelemetryAttributes.ErrorType, errorType);

        OperationsCompleted.Add(1, tagList);
        OperationDuration.Record(seconds, tagList);
    }

    /// <summary>
    /// Определяет категорию ошибки по типу исключения. Единственное место разбора
    /// на весь контур: категория одинакова в метрике, журнале и трассе.
    ///
    /// Типизированные ошибки контура несут категорию сами; для остальных применяется
    /// узкий разбор по типу.
    ///
    /// Голый тайм-аут после первого блока из производственного чтения тела не выходит:
    /// бюджет чтения преобразует его в типизированную ошибку в месте возникновения.
    /// Ветвь оставлена как страховка — её срабатывание означает нарушение контракта
    /// где-то ещё и требует разбирательства, а не расширения этой классификации.
    /// </summary>
    internal static string ClassifyError(Exception exception)
    {
        if (exception is MoexException moexException)
            return moexException.ErrorCategory;

        if (exception is HttpRequestException)
            return MoexErrorTypes.TransportError;

        if (exception is TimeoutException)
            return MoexErrorTypes.Timeout;

        return MoexErrorTypes.Unknown;
    }

    // ══════════════════════════════════════════════
    // Исторические задания загрузки
    // ══════════════════════════════════════════════

    /// <summary>
    /// Длительность исторического задания, в секундах. Границы покрывают всю
    /// наблюдаемую шкалу: от короткого дозагрузочного задания в десятки секунд
    /// до многочасовой загрузки широкого диапазона.
    /// </summary>
    public static readonly Histogram<double> LoadTaskDuration =
        Meter.CreateHistogram<double>(
            "moex.load.task.duration",
            unit: "s",
            description: "Historical load task duration",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = new double[]
                {
                    5, 15, 30, 60, 180, 300, 900, 1800, 3600, 7200, 14400
                }
            });

    /// <summary>Количество завершённых исторических заданий.</summary>
    public static readonly Counter<long> LoadTasksCompleted =
        Meter.CreateCounter<long>(
            "moex.load.tasks.completed",
            description: "Historical load tasks completed total");

    /// <summary>
    /// Текущее число заданий в работе. Счётчик двусторонний: увеличивается при
    /// фактическом получении владения задачей и уменьшается при выходе из
    /// защищённого жизненного цикла.
    /// </summary>
    public static readonly UpDownCounter<long> LoadTasksActive =
        Meter.CreateUpDownCounter<long>(
            "moex.load.tasks.active",
            description: "Historical load tasks currently running");
}

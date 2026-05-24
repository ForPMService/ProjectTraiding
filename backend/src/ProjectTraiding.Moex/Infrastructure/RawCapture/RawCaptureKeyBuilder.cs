namespace ProjectTraiding.Moex.Infrastructure.RawCapture;

/// <summary>
/// Строит ключи объектов S3 для raw-capture.
/// Чистые статические методы, без состояния, без зависимостей.
/// </summary>
public static class RawCaptureKeyBuilder
{
    /// <summary>
    /// Ключ для ошибочного ответа (режим FailedOnly).
    /// Пример: moex/errors/schema-mismatch/alg/candles/stock/SBER/request_date=2026-05-24/run=abc123/page=47.json
    /// </summary>
    /// <param name="errorType">Тип ошибки: schema-mismatch, parse-error, http-error, empty-data, boardid-mismatch.</param>
    /// <param name="client">Клиент: iss, alg, calendar, realtime.</param>
    /// <param name="dataType">Тип данных: candles, tradestats, obstats, securities, offdays, suspended и т.д.</param>
    /// <param name="market">Рынок: stock, futures. Null если не применимо (offdays-all).</param>
    /// <param name="secid">Тикер. Null если не применимо (calendar без тикера).</param>
    /// <param name="requestDate">Дата запроса.</param>
    /// <param name="runId">Идентификатор запуска.</param>
    /// <param name="fileName">Имя файла: page=0.json, response.json, date=2026-05-24.json.</param>
    public static string BuildErrorKey(
        string errorType,
        string client,
        string dataType,
        string? market,
        string? secid,
        DateOnly requestDate,
        string runId,
        string fileName)
    {
        return BuildKey("moex/errors/" + errorType, client, dataType, market, secid, requestDate, runId, fileName);
    }

    /// <summary>
    /// Ключ для успешного ответа (режимы Sample, All).
    /// Пример: moex/raw/alg/candles/stock/SBER/request_date=2026-05-24/run=abc123/page=0.json
    /// </summary>
    public static string BuildRawKey(
        string client,
        string dataType,
        string? market,
        string? secid,
        DateOnly requestDate,
        string runId,
        string fileName)
    {
        return BuildKey("moex/raw", client, dataType, market, secid, requestDate, runId, fileName);
    }

    /// <summary>
    /// Имя файла для страницы пагинации.
    /// </summary>
    public static string PageFileName(int pageNumber)
    {
        return "page=" + pageNumber.ToString() + ".json";
    }

    /// <summary>
    /// Имя файла для одностраничного ответа.
    /// </summary>
    public static string ResponseFileName()
    {
        return "response.json";
    }

    /// <summary>
    /// Имя файла для подневной загрузки (FUTOI).
    /// </summary>
    public static string DateFileName(DateOnly date)
    {
        return "date=" + date.ToString("yyyy-MM-dd") + ".json";
    }

    private static string BuildKey(
        string prefix,
        string client,
        string dataType,
        string? market,
        string? secid,
        DateOnly requestDate,
        string runId,
        string fileName)
    {
        // moex/errors/schema-mismatch/alg/candles/stock/SBER/request_date=2026-05-24/run=abc123/page=47.json
        // moex/raw/alg/candles/stock/SBER/request_date=2026-05-24/run=abc123/page=0.json

        string key = prefix + "/" + client + "/" + dataType;

        if (market is not null)
        {
            key = key + "/" + market;
        }

        if (secid is not null)
        {
            key = key + "/" + secid;
        }

        key = key + "/request_date=" + requestDate.ToString("yyyy-MM-dd")
            + "/run=" + runId
            + "/" + fileName;

        return key;
    }
}

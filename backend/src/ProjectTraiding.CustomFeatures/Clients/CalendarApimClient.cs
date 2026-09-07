using Microsoft.Extensions.Options;
using ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;
using ProjectTraiding.CustomFeatures.Infrastructure.Buffers;
using ProjectTraiding.CustomFeatures.Options;
using ProjectTraiding.CustomFeatures.Parsing;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.CustomFeatures.Clients;

public class CalendarApimClient
{
    private readonly CalendarSourceOptions _options;
    private readonly CalendarHttpTransport _transport;

    public CalendarApimClient(
        IOptions<CalendarSourceOptions> options,
        HttpClient httpClient,
        ILogger<CalendarApimClient> logger)
    {
        _options = options.Value;
        _transport = new CalendarHttpTransport(
            httpClient,
            logger,
            _options,
            _options.ApimBaseUrl,
            CalendarLogSources.Apim,
            requiresApiKey: true);
    }

    public async Task<string> GetRaw(
        string method,
        Dictionary<string, string>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _transport.SendWithoutStatusCheckAsync(method, queryParams, cancellationToken);

        using RentedBuffer rentedArr = await RentedBuffer.RentFromResponseAsync(
            response, _options.BodyReadTimeout, method, cancellationToken);

        return Encoding.UTF8.GetString(rentedArr.Span);
    }

    public async Task<List<CalendarOffDaysMarketDTO>> GetStockOffDays(
        DateOnly from,
        DateOnly till,
        CancellationToken ct)
    {
        const string endpoint = "/calendars/stock.json";
        using RentedBuffer buffer = await RentAsync(
            endpoint, CreateDateRangeQuery(from, till, true), ct);
        return ParsingCalendar.ParseOffDaysMarket(buffer.Memory);
    }

    public async Task<List<CalendarOffDaysMarketDTO>> GetFuturesOffDays(
        DateOnly from,
        DateOnly till,
        CancellationToken ct)
    {
        const string endpoint = "/calendars/futures.json";
        using RentedBuffer buffer = await RentAsync(
            endpoint, CreateDateRangeQuery(from, till, true), ct);
        return ParsingCalendar.ParseOffDaysMarket(buffer.Memory);
    }

    public async Task<List<FuturesExpirationDTO>>
        GetFuturesSecurities(DateOnly from, DateOnly till, CancellationToken ct)
    {
        const string endpoint = "/calendars/futures/securities.json";
        using RentedBuffer buffer = await RentAsync(
            endpoint, CreateDateRangeQuery(from, till, false), ct);
        return ParsingCalendar.ParseFuturesSecurities(buffer.Memory);
    }

    public async Task<List<RfudSecurityDTO>> GetActiveRfudSecIds(CancellationToken ct)
    {
        const string endpoint = "/engines/futures/markets/forts/boards/RFUD/securities.json?iss.meta=off";
        using RentedBuffer buffer = await RentAsync(endpoint, null, ct);
        return ParsingRfudSecurities.ParseSecIds(buffer.Memory);
    }

    // Параметр start этими методами источника не обрабатывается: проверено
    // ответами диагностики 7 сентября 2026 года, ответ при start=436 для stock
    // и start=13 для futures совпал с ответом без смещения. Метод отдаёт
    // расписание текущего дня целиком, блока session_schedule.cursor в ответе нет.
    // Цикл по страницам возвращать нельзя: он либо упрётся в повтор, либо
    // не закончится.
    // Отдельно, более ранней проверкой: запрос этих методов с исторической датой
    // возвращал текущее состояние, поэтому параметры from и till тоже не
    // передаются и метод не принимает дату снаружи.
    public async Task<List<TradingPeriodWriteDTO>> GetStockSessions(CancellationToken ct)
    {
        const string endpoint = "/calendars/stock/session.json";
        using RentedBuffer buffer = await RentAsync(endpoint, null, ct);
        return ParsingSessions.ParseStockSessions(buffer.Memory);
    }

    public async Task<List<TradingPeriodWriteDTO>> GetFuturesSessions(CancellationToken ct)
    {
        const string endpoint = "/calendars/futures/session.json";
        using RentedBuffer buffer = await RentAsync(endpoint, null, ct);
        return ParsingSessions.ParseFuturesSessions(buffer.Memory);
    }

    private async Task<RentedBuffer> RentAsync(
        string endpoint,
        Dictionary<string, string>? queryParams,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _transport.SendAsync(endpoint, queryParams, ct);
        return await RentedBuffer.RentFromResponseAsync(
            response, _options.BodyReadTimeout, endpoint, ct);
    }

    private static Dictionary<string, string> CreateDateRangeQuery(
        DateOnly from,
        DateOnly till,
        bool showAllDays)
    {
        Dictionary<string, string> queryParams = new()
        {
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["till"] = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        if (showAllDays)
            queryParams["show_all_days"] = "1";
        return queryParams;
    }
}

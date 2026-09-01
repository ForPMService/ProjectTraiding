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

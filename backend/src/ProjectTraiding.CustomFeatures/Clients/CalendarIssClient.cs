using Microsoft.Extensions.Options;
using ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;
using ProjectTraiding.CustomFeatures.Infrastructure.Buffers;
using ProjectTraiding.CustomFeatures.Options;
using ProjectTraiding.CustomFeatures.Parsing;
using System.Globalization;

namespace ProjectTraiding.CustomFeatures.Clients;

public class CalendarIssClient
{
    private readonly CalendarSourceOptions _options;
    private readonly CalendarHttpTransport _transport;

    public CalendarIssClient(
        IOptions<CalendarSourceOptions> options,
        HttpClient httpClient,
        ILogger<CalendarIssClient> logger)
    {
        _options = options.Value;
        _transport = new CalendarHttpTransport(
            httpClient,
            logger,
            _options,
            _options.IssBaseUrl,
            CalendarLogSources.Iss,
            requiresApiKey: false);
    }

    public async Task<List<EngineDailyTableDTO>> GetEngine(
        string engine,
        CancellationToken ct)
    {
        string endpoint = $"/engines/{engine}.json";
        using RentedBuffer buffer = await RentAsync(endpoint, null, ct);
        return ParsingIssCalendar.ParseEngine(buffer.Memory);
    }

    public async Task<List<ListingIntervalDTO>> GetListing(
        string engine,
        string market,
        string? status,
        int limit,
        int start,
        CancellationToken ct)
    {
        string endpoint = $"/history/engines/{engine}/markets/{market}/listing.json";
        Dictionary<string, string> queryParams = new()
        {
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["start"] = start.ToString(CultureInfo.InvariantCulture),
        };
        if (status is not null)
            queryParams["status"] = status;

        using RentedBuffer buffer = await RentAsync(endpoint, queryParams, ct);
        return ParsingIssCalendar.ParseListing(buffer.Memory);
    }

    public async Task<List<SplitWriteDTO>> GetSplits(CancellationToken ct)
    {
        const string endpoint = "/statistics/engines/stock/splits.json";
        using RentedBuffer buffer = await RentAsync(endpoint, null, ct);
        return ParsingIssCalendar.ParseSplits(buffer.Memory);
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
}

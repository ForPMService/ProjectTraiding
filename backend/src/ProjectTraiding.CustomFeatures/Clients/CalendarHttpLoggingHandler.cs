using System.Diagnostics;

namespace ProjectTraiding.CustomFeatures.Clients;

public class CalendarHttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<CalendarHttpLoggingHandler> _logger;
    private readonly string _source;

    public CalendarHttpLoggingHandler(ILogger<CalendarHttpLoggingHandler> logger, string source)
    {
        _logger = logger;
        _source = source;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Без схемы и узла: базовый адрес постоянен и не несёт сведений, а полный адрес
        // рискует вынести в журнал то, что в него попадать не должно. Параметры запроса сохраняются.
        string endpoint = request.RequestUri?.PathAndQuery ?? "unknown";
        long timeStart = Stopwatch.GetTimestamp();
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(timeStart);

        CalendarHttpLogMessages.HttpResponseReceived(
            _logger, _source, request.Method.Method, endpoint,
            response.StatusCode, elapsed, response.Content.Headers.ContentLength);

        return response;
    }
}

using System.Diagnostics;
using System.Net;
using ProjectTraiding.Moex.Infrastructure.Telemetry;

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpLoggingHandler : DelegatingHandler
    {
        private readonly ILogger<MoexHttpLoggingHandler> _logger;
        private readonly string _source;

        public MoexHttpLoggingHandler(ILogger<MoexHttpLoggingHandler> logger, string source)
        {
            _logger = logger;
            _source = source;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string endpoint = request.RequestUri?.ToString() ?? "unknown";

            MoexLogMessages.HttpRequestSent(_logger, _source, request.Method.Method, endpoint);
            MoexMetrics.HttpRequests.Add(1,
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source));

            long timeStart = Stopwatch.GetTimestamp();

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (Exception)
            {
                // Транспортная ошибка (DNS, connection refused, timeout до ответа).
                // Метрика длительности не записывается — ответа нет.
                MoexMetrics.HttpErrors.Add(1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.ErrorType, "transport_error"));
                throw;
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(timeStart);

            MoexLogMessages.HttpResponseReceived(
                _logger, _source, request.Method.Method, endpoint,
                response.StatusCode, elapsed, response.Content.Headers.ContentLength);

            MoexMetrics.HttpRequestDuration.Record(elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.StatusCode, (int)response.StatusCode));

            // Серверные ошибки (5xx) считаем в метрику ошибок.
            // 4xx (кроме 429) могут быть нормальным поведением — не считаем.
            // 429 обрабатывается Polly retry, метрика retry считается отдельно.
            if ((int)response.StatusCode >= 500)
            {
                MoexMetrics.HttpErrors.Add(1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.ErrorType, "server_error"),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.StatusCode, (int)response.StatusCode));
            }

            return response;
        }
    }
}

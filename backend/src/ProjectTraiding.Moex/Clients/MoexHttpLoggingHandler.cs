using ProjectTraiding.Moex.Infrastructure.Telemetry;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Clients
{
    public class MoexHttpLoggingHandler: DelegatingHandler
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
            // Без схемы и узла: базовый адрес постоянен и не несёт сведений, а полный адрес
            // рискует вынести в журнал то, что в него попадать не должно. Параметры запроса сохраняются.
            string endpoint = request.RequestUri?.PathAndQuery ?? "unknown";

            MoexLogMessages.HttpRequestSent(_logger, _source, request.Method.Method, endpoint);
            MoexMetrics.HttpRequests.Add(1,
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source));

            long timeStart = Stopwatch.GetTimestamp();

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Транспортная ошибка (DNS, connection refused, timeout до ответа).
                // Метрика длительности не записывается — ответа нет.
                MoexMetrics.HttpErrors.Add(1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.ErrorType, MoexErrorTypes.TransportError));
                throw;
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(timeStart);

            MoexLogMessages.HttpResponseReceived(
                _logger, _source, request.Method.Method, endpoint,
                response.StatusCode, elapsed, response.Content.Headers.ContentLength);

            MoexMetrics.HttpRequestDuration.Record(elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                new KeyValuePair<string, object?>(MoexTelemetryAttributes.StatusCode, (int)response.StatusCode));

            // Серверные ответы (5xx) и объявленный источником тайм-аут (408) считаем
            // в метрику ошибок. Прочие 4xx могут быть нормальным поведением — не считаем.
            // 429 обрабатывается слоем устойчивости, метрика повторов считается отдельно.
            int statusCode = (int)response.StatusCode;

            if (statusCode == 408)
            {
                MoexMetrics.HttpErrors.Add(1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.ErrorType, MoexErrorTypes.Timeout),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.StatusCode, statusCode));
            }
            else if (statusCode is >= 500 and <= 599)
            {
                MoexMetrics.HttpErrors.Add(1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _source),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.ErrorType, MoexErrorTypes.ServerError),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.StatusCode, statusCode));
            }

            return response;
        }

    }
}

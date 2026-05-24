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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string endpoint = request.RequestUri?.ToString() ?? "unknown";
            MoexLogMessages.HttpRequestSent(_logger, _source, request.Method.Method, endpoint);
            long timeStart = Stopwatch.GetTimestamp();
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            TimeSpan elapsedMs = Stopwatch.GetElapsedTime(timeStart);
            MoexLogMessages.HttpResponseReceived(_logger, _source, request.Method.Method, endpoint, response.StatusCode, elapsedMs, response.Content.Headers.ContentLength);
            return response;
        }

    }
}

using Polly.Timeout;
using ProjectTraiding.CustomFeatures.Errors;
using ProjectTraiding.CustomFeatures.Options;
using System.Net;

namespace ProjectTraiding.CustomFeatures.Clients;

public sealed class CalendarHttpTransport
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly string _logSource;
    private readonly bool _requiresApiKey;
    private readonly string? _authorizationHeader;

    public CalendarHttpTransport(
        HttpClient httpClient,
        ILogger logger,
        CalendarSourceOptions options,
        string baseUrl,
        string logSource,
        bool requiresApiKey)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = baseUrl;
        _logSource = logSource;
        _requiresApiKey = requiresApiKey;
        _authorizationHeader = requiresApiKey && !string.IsNullOrWhiteSpace(options.AlgKey)
            ? "Bearer " + options.AlgKey
            : null;
    }

    public async Task<HttpResponseMessage> SendAsync(
        string method,
        Dictionary<string, string>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        string requestUrl = BuildRequestUrl(method, queryParams);
        using HttpRequestMessage request = CreateRequest(requestUrl);
        try
        {
            HttpResponseMessage response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            CalendarHttpClientHelpers.EnsureSuccessOrThrow(response, method);
            return response;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            CalendarTimeoutException timeoutEx = new CalendarTimeoutException(
                $"MOEX request timeout for {method}", method, "http_client", ex);
            CalendarHttpLogMessages.RequestFailed(_logger, timeoutEx, _logSource, method,
                timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
            throw timeoutEx;
        }
        catch (TimeoutRejectedException ex)
        {
            CalendarTimeoutException timeoutEx = new CalendarTimeoutException(
                $"MOEX attempt timeout for {method}", method, "polly_attempt", ex);
            CalendarHttpLogMessages.RequestFailed(_logger, timeoutEx, _logSource, method,
                timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
            throw timeoutEx;
        }
        catch (CalendarHttpException ex)
        {
            CalendarHttpLogMessages.RequestFailed(_logger, ex, _logSource, method,
                ex.ErrorCategory, (HttpStatusCode?)ex.StatusCode,
                (ex as CalendarTimeoutException)?.TimeoutSource, ex.Message);
            throw;
        }
    }

    public async Task<HttpResponseMessage> SendWithoutStatusCheckAsync(
        string method,
        Dictionary<string, string>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        string requestUrl = BuildRequestUrl(method, queryParams);
        using HttpRequestMessage request = CreateRequest(requestUrl);
        return await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private string BuildRequestUrl(string method, Dictionary<string, string>? queryParams)
    {
        if (queryParams is not { Count: > 0 })
            return _baseUrl + method;

        QueryString queryString = QueryString.Create(
            queryParams.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
        return _baseUrl + method + queryString.ToString();
    }

    private HttpRequestMessage CreateRequest(string requestUrl)
    {
        if (_requiresApiKey && _authorizationHeader is null)
            throw new InvalidOperationException(
                "MOEX ALGOPACK API key is not configured. Set MoexAlg:Key via user-secrets or environment variable.");

        HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
        if (_authorizationHeader is not null)
            request.Headers.Add("Authorization", _authorizationHeader);

        return request;
    }
}

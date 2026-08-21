using Microsoft.Extensions.Logging;
using Polly.Timeout;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using System.Net;

namespace ProjectTraiding.Moex.Clients
{
    /// <summary>
    /// Общий транспорт обращения к Московской бирже: сборка адреса, заголовок доступа,
    /// отправка, классификация ответа и запись отказа в журнал. Предметный разбор ответа
    /// транспорту не принадлежит — он остаётся у клиентов.
    ///
    /// Транспорт намеренно не регистрируется в контейнере, а создаётся каждым клиентом
    /// в его конструкторе. Каждый клиент типизированный, со своим HttpClient и своей
    /// цепочкой обработчиков — устойчивость с собственным размыкателем цепи, общий
    /// ограничитель частоты, журналирование, — настроенной под свою константу источника.
    /// Одна общая служба схлопнула бы четыре цепочки в одну и изменила бы поведение
    /// при отказах.
    /// </summary>
    public sealed class MoexHttpTransport
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly MoexOptions _options;
        private readonly string _baseUrl;
        private readonly string _logSource;
        private readonly bool _requiresApiKey;

        /// <summary>
        /// Готовое значение заголовка доступа. Собирается один раз на транспорт: склейка
        /// на каждый запрос создавала бы в куче строку с ключом на каждое обращение к бирже.
        /// Пусто, когда ключ не требуется или не настроен; проверку настроенности
        /// по-прежнему выполняет EnsureApiKeyConfigured в момент создания запроса.
        /// </summary>
        private readonly string? _authorizationHeader;

        /// <param name="baseUrl">Базовый адрес: APIM для платного доступа, ISS для публичного.</param>
        /// <param name="logSource">Константа источника из <see cref="MoexLogSources"/>.</param>
        /// <param name="requiresApiKey">
        /// Требуется ли заголовок доступа. У публичного ISS ключа нет, и отсутствие
        /// значения в настройках для него не является ошибкой.
        /// </param>
        public MoexHttpTransport(
            HttpClient httpClient,
            ILogger logger,
            MoexOptions options,
            string baseUrl,
            string logSource,
            bool requiresApiKey)
        {
            _httpClient = httpClient;
            _logger = logger;
            _options = options;
            _baseUrl = baseUrl;
            _logSource = logSource;
            _requiresApiKey = requiresApiKey;
            _authorizationHeader = requiresApiKey && !string.IsNullOrWhiteSpace(options.AlgKey)
                ? "Bearer " + options.AlgKey
                : null;
        }

        /// <summary>
        /// Отправляет запрос, проверяет статус и переводит отказ в типизированное исключение.
        /// Ответ возвращается непрочитанным: тело читает вызывающий.
        /// </summary>
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
                HttpClientHelpers.EnsureSuccessOrThrow(response, method);
                return response;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                MoexTimeoutException timeoutEx = new MoexTimeoutException(
                    $"MOEX request timeout for {method}", method, "http_client", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, _logSource, method,
                    timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (TimeoutRejectedException ex)
            {
                MoexTimeoutException timeoutEx = new MoexTimeoutException(
                    $"MOEX attempt timeout for {method}", method, "polly_attempt", null, ex);
                MoexLogMessages.RequestFailed(_logger, timeoutEx, _logSource, method,
                    timeoutEx.ErrorCategory, null, timeoutEx.TimeoutSource, timeoutEx.Message);
                throw timeoutEx;
            }
            catch (MoexHttpException ex)
            {
                // Источник тайм-аута берётся из самого исключения: при статусе 408
                // EnsureSuccessOrThrow бросает MoexTimeoutException с заполненным
                // TimeoutSource. У прочих отказов приведение даёт пустое значение.
                MoexLogMessages.RequestFailed(_logger, ex, _logSource, method,
                    ex.ErrorCategory, (HttpStatusCode?)ex.StatusCode,
                    (ex as MoexTimeoutException)?.TimeoutSource, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Отправляет запрос без проверки статуса и без записи отказа в журнал.
        /// Нужен диагностическому получению исходного тела,
        /// которое существует ровно ради того, чтобы увидеть тело ошибки биржи так же,
        /// как тело успешного ответа.
        /// </summary>
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
            // Пустой словарь ради проверки числа записей больше не создаётся. Обращения без
            // параметров есть у карточек инструментов и календаря, и их готовые пути уже
            // содержат знак вопроса — дописывать к ним нечего.
            if (queryParams is not { Count: > 0 })
                return _baseUrl + method;

            QueryString queryString = QueryString.Create(queryParams);
            return _baseUrl + method + queryString.ToString();
        }

        private HttpRequestMessage CreateRequest(string requestUrl)
        {
            if (_requiresApiKey)
                EnsureApiKeyConfigured();

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            if (_requiresApiKey)
                request.Headers.Add("Authorization", _authorizationHeader!);

            return request;
        }

        private void EnsureApiKeyConfigured()
        {
            if (string.IsNullOrWhiteSpace(_options.AlgKey))
            {
                throw new InvalidOperationException(
                    "MOEX ALGOPACK API key is not configured. Set MoexAlg:Key via user-secrets or environment variable.");
            }
        }
    }
}

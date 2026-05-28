using System.Net;
using System.Net.Http.Headers;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Clients.Errors;

namespace TestHistoryData;

public class TypedErrorsTests
{
    // ═══════════════════════════════════════════════════════════
    // 1. 429 → MoexRateLimitException, IsRetryable = true
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns429_ThrowsMoexRateLimitException()
    {
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);

        MoexRateLimitException ex = await Assert.ThrowsAsync<MoexRateLimitException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test/endpoint"));

        Assert.True(ex.IsRetryable);
    }

    // ═══════════════════════════════════════════════════════════
    // 2. 429 + Retry-After: Delta → парсится в RetryAfter
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns429WithRetryAfterSeconds_ParsesCorrectly()
    {
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));

        MoexRateLimitException ex = await Assert.ThrowsAsync<MoexRateLimitException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test/endpoint"));

        Assert.Equal(TimeSpan.FromSeconds(5), ex.RetryAfter);
    }

    // ═══════════════════════════════════════════════════════════
    // 3. 429 без Retry-After → ex.RetryAfter == null
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns429WithoutRetryAfter_RetryAfterIsNull()
    {
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);

        MoexRateLimitException ex = await Assert.ThrowsAsync<MoexRateLimitException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test/endpoint"));

        Assert.Null(ex.RetryAfter);
    }

    // ═══════════════════════════════════════════════════════════
    // 4. 401 → MoexAuthException, IsRetryable = false
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns401_ThrowsMoexAuthException_NotRetryable()
    {
        HttpResponseMessage response = new(HttpStatusCode.Unauthorized);

        MoexAuthException ex = await Assert.ThrowsAsync<MoexAuthException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test/endpoint"));

        Assert.False(ex.IsRetryable);
    }

    // ═══════════════════════════════════════════════════════════
    // 5. 403 → MoexAuthException, IsRetryable = false
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns403_ThrowsMoexAuthException_NotRetryable()
    {
        HttpResponseMessage response = new(HttpStatusCode.Forbidden);

        MoexAuthException ex = await Assert.ThrowsAsync<MoexAuthException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test/endpoint"));

        Assert.False(ex.IsRetryable);
    }

    // ═══════════════════════════════════════════════════════════
    // 6. 400 → MoexBadRequestException, IsRetryable = false
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns400_ThrowsMoexBadRequestException()
    {
        HttpResponseMessage response = new(HttpStatusCode.BadRequest);

        MoexBadRequestException ex = await Assert.ThrowsAsync<MoexBadRequestException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test/endpoint"));

        Assert.False(ex.IsRetryable);
    }

    // ═══════════════════════════════════════════════════════════
    // 7. 404 → MoexNotFoundException, IsRetryable = false
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns404_ThrowsMoexNotFoundException()
    {
        HttpResponseMessage response = new(HttpStatusCode.NotFound);

        MoexNotFoundException ex = await Assert.ThrowsAsync<MoexNotFoundException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test/endpoint"));

        Assert.False(ex.IsRetryable);
    }

    // ═══════════════════════════════════════════════════════════
    // 8. 500 → MoexServerException, IsRetryable = true
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns500_ThrowsMoexServerException_Retryable()
    {
        HttpResponseMessage response = new(HttpStatusCode.InternalServerError);

        MoexServerException ex = await Assert.ThrowsAsync<MoexServerException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test/endpoint"));

        Assert.True(ex.IsRetryable);
    }

    // ═══════════════════════════════════════════════════════════
    // 9. 200 → не бросает, response не disposed
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Returns200_DoesNotThrow()
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent("ok")
        };

        await HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test");

        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ok", body);
    }

    // ═══════════════════════════════════════════════════════════
    // 10. Ошибочный ответ — response.Dispose() вызывается до throw
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task EnsureSuccessOrThrowAsync_DisposesResponseOnError()
    {
        HttpResponseMessage response = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("body")
        };

        await Assert.ThrowsAsync<MoexBadRequestException>(
            () => HttpClientHelpers.EnsureSuccessOrThrowAsync(response, "test"));

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            using Stream _ = await response.Content.ReadAsStreamAsync();
        });
    }

    // ═══════════════════════════════════════════════════════════
    // ErrorCategory — каждый наследник возвращает стабильную строку
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(typeof(MoexRateLimitException), "rate_limit")]
    [InlineData(typeof(MoexServerException), "server_error")]
    [InlineData(typeof(MoexAuthException), "auth")]
    [InlineData(typeof(MoexBadRequestException), "bad_request")]
    [InlineData(typeof(MoexNotFoundException), "not_found")]
    [InlineData(typeof(MoexClientException), "client_error")]
    [InlineData(typeof(MoexUnexpectedStatusException), "unexpected_status")]
    public void ErrorCategory_MatchesExpected(Type exceptionType, string expectedCategory)
    {
        // Все наследники имеют конструктор (string endpoint, int statusCode)
        // кроме RateLimit (endpoint, retryAfter?) — обрабатываем отдельно
        MoexHttpException ex = exceptionType.Name switch
        {
            nameof(MoexRateLimitException) => new MoexRateLimitException("test", null),
            nameof(MoexBadRequestException) => new MoexBadRequestException("test"),
            nameof(MoexNotFoundException) => new MoexNotFoundException("test"),
            _ => (MoexHttpException)Activator.CreateInstance(exceptionType, "test", 500)!
        };

        Assert.Equal(expectedCategory, ex.ErrorCategory);
    }

    [Fact]
    public void TimeoutException_ErrorCategory_IsTimeout()
    {
        var ex = new MoexTimeoutException("msg", "test", "http_client");
        Assert.Equal("timeout", ex.ErrorCategory);
    }

    // ═══════════════════════════════════════════════════════════
    // TimeoutSource — различает http_client и polly_attempt
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void TimeoutException_HttpClient_HasCorrectSource()
    {
        var inner = new TaskCanceledException();
        var ex = new MoexTimeoutException("request timeout", "/test", "http_client", TimeSpan.FromSeconds(30), inner);

        Assert.Equal("http_client", ex.TimeoutSource);
        Assert.Equal("timeout", ex.ErrorCategory);
        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public void TimeoutException_PollyAttempt_HasCorrectSource()
    {
        var ex = new MoexTimeoutException("attempt timeout", "/test", "polly_attempt");

        Assert.Equal("polly_attempt", ex.TimeoutSource);
        Assert.Null(ex.Timeout);
    }

    // ═══════════════════════════════════════════════════════════
    // TryParseRetryAfter — краевые случаи
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void TryParseRetryAfter_NoHeader_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        Assert.Null(HttpClientHelpers.TryParseRetryAfter(response));
    }

    [Fact]
    public void TryParseRetryAfter_DeltaSeconds_ReturnsDelta()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), HttpClientHelpers.TryParseRetryAfter(response));
    }

    [Fact]
    public void TryParseRetryAfter_HttpDateInFuture_ReturnsPositiveDelta()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var futureDate = DateTimeOffset.UtcNow.AddSeconds(60);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(futureDate);

        var result = HttpClientHelpers.TryParseRetryAfter(response);

        Assert.NotNull(result);
        Assert.True(result!.Value > TimeSpan.Zero);
        Assert.True(result.Value <= TimeSpan.FromSeconds(61)); // запас на время выполнения
    }

    [Fact]
    public void TryParseRetryAfter_HttpDateInPast_ReturnsZero()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var pastDate = DateTimeOffset.UtcNow.AddSeconds(-10);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(pastDate);

        var result = HttpClientHelpers.TryParseRetryAfter(response);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.Zero, result!.Value);
    }

    // ═══════════════════════════════════════════════════════════
    // GetRetryAfterForPolly — clamp и минимум
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GetRetryAfterForPolly_LargeValue_ClampedToMaxDelay()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));

        var result = HttpClientHelpers.GetRetryAfterForPolly(response, TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), result);
    }

    [Fact]
    public void GetRetryAfterForPolly_PastDate_ReturnsOneSecond()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var pastDate = DateTimeOffset.UtcNow.AddSeconds(-30);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(pastDate);

        var result = HttpClientHelpers.GetRetryAfterForPolly(response, TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromSeconds(1), result);
    }

    [Fact]
    public void GetRetryAfterForPolly_NoHeader_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var result = HttpClientHelpers.GetRetryAfterForPolly(response, TimeSpan.FromMinutes(2));

        Assert.Null(result);
    }
}

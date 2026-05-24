using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Clients.Errors;
using ProjectTraiding.Moex.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.RateLimiting;

namespace TestHistoryData
{
    public class RateLimiterTests
    {
        // ═══════════════════════════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════════════════════════

        private class FakeHandler : HttpMessageHandler
        {
            public int CallCount;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref CallCount);
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }

        private static TokenBucketRateLimiter CreateLimiter(
            int tokenLimit = 8,
            int queueLimit = 64)
        {
            return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = tokenLimit,
                TokensPerPeriod = tokenLimit,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = queueLimit,
            });
        }

        private static (HttpClient client, FakeHandler fake) CreateTestClient(
            RateLimiter limiter,
            MoexOptions? options = null)
        {
            options ??= new MoexOptions();
            var fake = new FakeHandler();
            var handler = new MoexRateLimitHandler(
                limiter,
                options,
                NullLogger<MoexRateLimitHandler>.Instance)
            {
                InnerHandler = fake
            };
            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://test.moex.com")
            };
            return (client, fake);
        }

        // ═══════════════════════════════════════════════════════════
        // 1. Burst в пределах лимита — все проходят без ожидания
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 8 запросов при TokenLimit=8. Все должны пройти мгновенно,
        /// потому что в ведре ровно 8 жетонов.
        /// Проверяем, что FakeHandler получил все 8 запросов.
        /// </summary>
        [Fact]
        public async Task BurstWithinLimit_AllRequestsPassImmediately()
        {
            using var limiter = CreateLimiter(tokenLimit: 8);
            var (client, fake) = CreateTestClient(limiter);

            for (int i = 0; i < 8; i++)
            {
                await client.GetAsync("/iss/test");
            }

            Assert.Equal(8, fake.CallCount);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. Burst сверх лимита — лишние запросы ждут
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// TokenLimit=2, отправляем 4 запроса параллельно.
        /// Все 4 должны пройти (очередь = 64, жетоны пополняются),
        /// но суммарное время > 0, потому что 3-й и 4-й ждали жетон.
        /// </summary>
        [Fact]
        public async Task BurstOverLimit_ExcessRequestsAreQueued()
        {
            // Маленький лимит, чтобы увидеть ожидание.
            using var limiter = CreateLimiter(tokenLimit: 2);
            var (client, fake) = CreateTestClient(limiter);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, 4)
                .Select(_ => client.GetAsync("/iss/test"))
                .ToArray();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Все 4 прошли — никто не отклонён, очередь не переполнена.
            Assert.Equal(4, fake.CallCount);

            // 2 жетона сразу, 2 ждали пополнения (≈1 секунда).
            // Проверяем, что было ожидание (не мгновенно).
            Assert.True(stopwatch.ElapsedMilliseconds > 500,
                $"Expected queuing delay, but completed in {stopwatch.ElapsedMilliseconds}ms");
        }

        // ═══════════════════════════════════════════════════════════
        // 3. Один limiter на все клиенты — суммарно не превышают лимит
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Два HttpClient делят один limiter с TokenLimit=4.
        /// Каждый отправляет по 2 запроса = 4 суммарно.
        /// 5-й запрос от любого клиента должен ждать.
        /// Проверяем, что limiter считает запросы от обоих клиентов вместе.
        /// </summary>
        [Fact]
        public async Task SharedLimiter_CountsAcrossClients()
        {
            using var limiter = CreateLimiter(tokenLimit: 4);
            var (clientA, fakeA) = CreateTestClient(limiter);
            var (clientB, fakeB) = CreateTestClient(limiter);

            // 4 запроса суммарно — ровно заполняют ведро.
            await clientA.GetAsync("/iss/test");
            await clientA.GetAsync("/iss/test");
            await clientB.GetAsync("/iss/test");
            await clientB.GetAsync("/iss/test");

            Assert.Equal(2, fakeA.CallCount);
            Assert.Equal(2, fakeB.CallCount);

            // 5-й запрос — жетонов нет, будет ждать пополнения.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await clientA.GetAsync("/iss/test");
            stopwatch.Stop();

            Assert.Equal(3, fakeA.CallCount);
            Assert.True(stopwatch.ElapsedMilliseconds > 200,
                $"5th request should have waited, but took {stopwatch.ElapsedMilliseconds}ms");
        }

        // ═══════════════════════════════════════════════════════════
        // 4. Переполнение очереди → MoexRateLimitRejectedException
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// TokenLimit=1, QueueLimit=0.
        /// Первый запрос забирает единственный жетон.
        /// Второй — очередь полна (QueueLimit=0), мгновенный отказ.
        /// </summary>
        [Fact]
        public async Task QueueOverflow_ThrowsRejectedException()
        {
            // QueueLimit=0: если жетонов нет — отказ без ожидания.
            using var limiter = CreateLimiter(tokenLimit: 1, queueLimit: 0);
            var (client, fake) = CreateTestClient(limiter);

            // Первый запрос — забирает единственный жетон.
            await client.GetAsync("/iss/test");
            Assert.Equal(1, fake.CallCount);

            // Второй — жетонов нет, очередь нулевая, отказ.
            var ex = await Assert.ThrowsAsync<MoexRateLimitRejectedException>(
                () => client.GetAsync("/iss/test"));

            Assert.Equal("queue_full", ex.Reason);
            Assert.Equal(1, fake.CallCount); // второй запрос НЕ ушёл в сеть
        }

        // ═══════════════════════════════════════════════════════════
        // 5. CancellationToken во время ожидания — запрос не уходит
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// TokenLimit=1, QueueLimit=10. Первый запрос забирает жетон.
        /// Второй встаёт в очередь. Отменяем CancellationToken.
        /// Ожидание: OperationCanceledException, запрос не ушёл.
        /// </summary>
        [Fact]
        public async Task CancellationDuringWait_DoesNotSendRequest()
        {
            using var limiter = CreateLimiter(tokenLimit: 1, queueLimit: 10);
            var (client, fake) = CreateTestClient(limiter);

            // Забираем единственный жетон.
            await client.GetAsync("/iss/test");
            Assert.Equal(1, fake.CallCount);

            // Второй запрос встанет в очередь. Отменяем через 100 мс.
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetAsync("/iss/test", cts.Token));

            // Запрос не ушёл в FakeHandler.
            Assert.Equal(1, fake.CallCount);
        }

        // ═══════════════════════════════════════════════════════════
        // 6. AcquireTimeout — бросает MoexRateLimitRejectedException
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// TokenLimit=1, AcquireTimeout=200ms. Первый запрос забирает жетон.
        /// Второй ждёт 200 мс и получает отказ по таймауту.
        /// </summary>
        [Fact]
        public async Task AcquireTimeout_ThrowsRejectedException()
        {
            using var limiter = CreateLimiter(tokenLimit: 1, queueLimit: 10);
            var options = new MoexOptions
            {
                RateLimitAcquireTimeout = TimeSpan.FromMilliseconds(200)
            };
            var (client, fake) = CreateTestClient(limiter, options);

            // Забираем единственный жетон.
            await client.GetAsync("/iss/test");

            // Второй — ждёт жетон, не дожидается за 200 мс.
            var ex = await Assert.ThrowsAsync<MoexRateLimitRejectedException>(
                () => client.GetAsync("/iss/test"));

            Assert.Equal("acquire_timeout", ex.Reason);
            Assert.NotNull(ex.WaitTime);
            Assert.Equal(1, fake.CallCount); // второй запрос не ушёл
        }

        // ═══════════════════════════════════════════════════════════
        // 7. Retry-попытки тоже расходуют permits
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Имитируем retry: отправляем 3 запроса последовательно через один limiter
        /// с TokenLimit=3. Все 3 проходят. 4-й ждёт.
        /// Это моделирует сценарий: 1 оригинальный запрос + 2 retry = 3 permits.
        ///
        /// Мы не подключаем Polly в тесте — смысл теста в том, что handler
        /// берёт permit на каждый вызов SendAsync, а Polly вызывает SendAsync
        /// для каждого retry. Три вызова = три permits.
        /// </summary>
        [Fact]
        public async Task EachCallConsumesPermit_SimulatesRetryBehavior()
        {
            using var limiter = CreateLimiter(tokenLimit: 3, queueLimit: 0);
            var (client, fake) = CreateTestClient(limiter);

            // 3 запроса — имитация: original + retry1 + retry2.
            await client.GetAsync("/iss/test");
            await client.GetAsync("/iss/test");
            await client.GetAsync("/iss/test");

            Assert.Equal(3, fake.CallCount);

            // 4-й — жетонов нет, QueueLimit=0 → отказ.
            await Assert.ThrowsAsync<MoexRateLimitRejectedException>(
                () => client.GetAsync("/iss/test"));

            Assert.Equal(3, fake.CallCount); // 4-й не прошёл
        }
    }
}

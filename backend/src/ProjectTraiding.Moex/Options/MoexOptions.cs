namespace ProjectTraiding.Moex.Options
{
    /// <summary>
    /// Все настройки MOEX-источника в одном месте.
    /// ISS = публичный API (iss.moex.com), без ключа.
    /// APIM = платный API (apim.moex.com), с Bearer-токеном. Используется ALGOPACK и Calendar.
    /// </summary>
    public class MoexOptions
    {
        /// <summary>Base URL для ISS (без ключа). Default: https://iss.moex.com/iss</summary>
        public string IssBaseUrl { get; set; } = "https://iss.moex.com/iss";

        /// <summary>Base URL для APIM (ALGOPACK + Calendar). Default: https://apim.moex.com/iss</summary>
        public string ApimBaseUrl { get; set; } = "https://apim.moex.com/iss";

        /// <summary>Bearer-токен для APIM. Через user-secrets или переменные окружения.</summary>
        public string AlgKey { get; set; } = string.Empty;

        /// <summary>Таймаут одного HTTP-запроса.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Предельное время на ЧТЕНИЕ тела ответа (фаза после получения заголовков).
        /// Отдельный сторож от RequestTimeout: общий HttpClient.Timeout снимается (В1),
        /// и без этого предела зависшее чтение тела висело бы бесконечно. Рвётся
        /// типизированной MoexTimeoutException с источником "body_read", запрос повторяем.
        /// </summary>
        public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public int MaxConnectionsPerServer { get; set; } = 10;
        public int MaxPagesPerLoad { get; set; } = 10_000;

        // ── Rate Limiter ────────────────────────────────────

        /// <summary>
        /// Максимум HTTP-запросов к MOEX в секунду (все клиенты суммарно).
        /// MOEX ≈ 10 req/sec на IP (частная переписка, май 2026).
        /// Ставим 8, чтобы оставить запас и не ходить по краю.
        /// </summary>
        public int MaxRequestsPerSecond { get; set; } = 10;

        /// <summary>
        /// Максимум запросов, ожидающих жетон в очереди rate limiter.
        /// Если очередь полна — мгновенный отказ MoexRateLimitRejectedException.
        /// 64 — достаточно для нескольких параллельных пагинаций,
        /// но не даёт копить тысячу запросов при зависании.
        /// </summary>
        public int RateLimitQueueLimit { get; set; } = 64;

        /// <summary>
        /// Сколько запрос ждёт жетон, прежде чем получить отказ.
        /// Если за это время жетон не появился — MoexRateLimitRejectedException.
        /// 30 секунд — долго ждать одного жетона, значит что-то сильно не так.
        /// </summary>
        public TimeSpan RateLimitAcquireTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Число одновременных дорожек загрузки в фоновом исполнителе.
        /// Потолок темпа — общий ограничитель частоты, поэтому при задержке страницы около
        /// двухсот миллисекунд полезны примерно три дорожки; четыре — осторожный запас.
        /// Не должно превышать MaxConnectionsPerServer (пул соединений).
        /// </summary>
        public int LoadWorkerConcurrency { get; set; } = 4;
    }
}

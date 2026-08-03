namespace ProjectTraiding.Moex.Infrastructure
{
    /// <summary>
    /// Московское время — единственный часовой пояс, в котором биржа отдаёт и принимает данные.
    /// Часовой пояс машины к делу не относится: приёмник на сервере в любом городе обязан
    /// спрашивать биржу о московском торговом дне.
    ///
    /// Без этого типа DateTime.Today на машине западнее Москвы даёт вчерашнюю дату в те часы,
    /// когда в Москве уже наступил новый день — а вечерняя сессия торгуется до 23:49 по Москве.
    /// Потеря была бы тихой: сутки данных в никуда, без единой ошибки в журнале.
    ///
    /// Смещение задано числом, а не поиском в базе часовых поясов: Москва не переводит часы
    /// с 2014 года, а поиск требует tzdata, которой в урезанном образе может не оказаться.
    /// Вернут перевод часов — менять здесь, в одном месте.
    /// </summary>
    public static class MoexTime
    {
        public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

        /// <summary>Текущий момент по московским часам.</summary>
        public static DateTime Now =>
            DateTime.SpecifyKind(DateTime.UtcNow + Offset, DateTimeKind.Unspecified);

        /// <summary>
        /// Приводит московское настенное время к типу со смещением. Существует потому,
        /// что прямое преобразование берёт часовой пояс машины: на сервере во всемирном
        /// времени рыночное время уехало бы на три часа, и заметить это было бы нечем.
        /// </summary>
        public static DateTimeOffset ToDateTimeOffset(DateTime moscowWallTime)
        {
            return new DateTimeOffset(
                DateTime.SpecifyKind(moscowWallTime, DateTimeKind.Unspecified),
                Offset);
        }

        /// <summary>Текущий торговый день по московским часам.</summary>
        public static DateOnly Today => DateOnly.FromDateTime(Now);
    }
}

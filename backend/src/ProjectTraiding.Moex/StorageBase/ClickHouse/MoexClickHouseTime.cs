using System;
using System.Globalization;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Построение меток времени для вставки в ClickHouse. Собирает ключевое время строки
    /// из строковых даты и времени торгов и переводит произвольную метку в московское
    /// стенное представление без сдвига. Состояния нет — только чистые преобразования.
    /// </summary>
    public static class MoexClickHouseTime
    {
        // Дата "yyyy-MM-dd" и время "HH:mm:ss" из источника в одно московское стенное время.
        // Kind=Unspecified — bulk insert трактует его как стенное время зоны столбца без сдвига.
        public static DateTime BuildSourceTime(string? tradeDate, string? tradeTime)
        {
            if (string.IsNullOrWhiteSpace(tradeDate) || string.IsNullOrWhiteSpace(tradeTime))
                throw new InvalidOperationException(
                    "Строка статистики отвергнута: пустые дата или время торгов.");

            DateTime parsed = DateTime.ParseExact(
                $"{tradeDate} {tradeTime}",
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);

            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        public static DateTime? AsWallClock(DateTime? value)
        {
            if (value is null)
                return null;

            return DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Метка "yyyy-MM-dd HH:mm:ss" из ответа реального времени в московское стенное время.
        /// Пустое значение — не ошибка: столбец необязательный.
        /// </summary>
        public static DateTime? ParseWallClock(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            DateTime parsed = DateTime.ParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);

            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Дата "yyyy-MM-dd" из ответа реального времени. Пустое значение — не ошибка.
        /// </summary>
        public static DateOnly? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}

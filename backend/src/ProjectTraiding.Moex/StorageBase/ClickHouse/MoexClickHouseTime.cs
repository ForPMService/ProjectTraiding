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

            // Дешёвый путь: обе строки уже получены, склеивать их ради разбора по образцу
            // незачем. Разбор идёт по фиксированным позициям и промежуточной строки не создаёт.
            if (TryBuildSourceTimeFast(tradeDate, tradeTime, out DateTime fast))
                return fast;

            // Откат. Всё, чего дешёвый путь не распознал, разбирает прежний разбор по образцу:
            // он же формирует отказ, поэтому тип и текст исключения остаются прежними.
            DateTime parsed = DateTime.ParseExact(
                $"{tradeDate} {tradeTime}",
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);

            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        // Формат источника: дата "yyyy-MM-dd" ровно 10 символов, время "HH:mm:ss" ровно 8.
        // Любое отклонение — не отказ, а повод отдать значение прежнему разбору.
        private static bool TryBuildSourceTimeFast(string date, string time, out DateTime value)
        {
            value = default;

            if (date.Length != 10 || time.Length != 8)
                return false;

            if (date[4] != '-' || date[7] != '-' || time[2] != ':' || time[5] != ':')
                return false;

            if (!TryReadDigits(date, 0, 4, out int year)
                || !TryReadDigits(date, 5, 2, out int month)
                || !TryReadDigits(date, 8, 2, out int day)
                || !TryReadDigits(time, 0, 2, out int hour)
                || !TryReadDigits(time, 3, 2, out int minute)
                || !TryReadDigits(time, 6, 2, out int second))
            {
                return false;
            }

            if (year < 1 || year > 9999 || month < 1 || month > 12)
                return false;

            if (day < 1 || day > DateTime.DaysInMonth(year, month))
                return false;

            if (hour > 23 || minute > 59 || second > 59)
                return false;

            value = new DateTime(
                year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            return true;
        }

        private static bool TryReadDigits(string text, int start, int length, out int value)
        {
            value = 0;
            for (int i = start; i < start + length; i++)
            {
                char symbol = text[i];
                if (symbol < '0' || symbol > '9')
                    return false;

                value = value * 10 + (symbol - '0');
            }

            return true;
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

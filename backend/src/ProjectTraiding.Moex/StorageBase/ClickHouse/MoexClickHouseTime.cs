using System;
using System.Globalization;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Построение меток времени для вставки в ClickHouse. Общий доменный модуль для всех карт
    /// столбцов: собирает ключевое время строки из строковых даты и времени торгов и переводит
    /// произвольную метку в московское стенное представление без сдвига. Вынесено из карт,
    /// где оба метода дословно повторялись. Состояния нет — только чистые преобразования.
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
    }
}

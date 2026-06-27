using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Описание формы вставки одного вида данных в ClickHouse: целевая таблица, порядок и типы
    /// столбцов, имя вида и зерно для токена. Превращение одной строки источника типа T в строку
    /// вставки по позициям и извлечение её метки времени — забота реализации. Писатель работает
    /// только через этот контракт и тип источника не знает.
    /// </summary>
    /// <typeparam name="T">Тип объекта передачи данных одной строки источника.</typeparam>
    public interface IRowMap<in T>
    {
        /// <summary>Целевая таблица ClickHouse.</summary>
        string Table { get; }

        /// <summary>Порядок столбцов строго по миграции.</summary>
        IReadOnlyList<string> Columns { get; }

        /// <summary>Типы столбцов по имени (для InsertOptions.ColumnTypes).</summary>
        IReadOnlyDictionary<string, string> ColumnTypes { get; }

        /// <summary>Префикс токена: вид данных и зерно, например "candles:1m" или "tradestats:5m".</summary>
        string TokenPrefix { get; }

        /// <summary>
        /// Проверка параметров диапазона один раз до цикла (например, непустой secid).
        /// </summary>
        void EnsureRangeValid(string secid);

        /// <summary>
        /// Строка источника и secid в строку вставки по позициям. Вызывает построчные стражи.
        /// Возвращает строку и её метку времени (для границ пачки в токене).
        /// </summary>
        (object?[] Row, DateTime Time) ToRow(T item, string secid);
    }
}

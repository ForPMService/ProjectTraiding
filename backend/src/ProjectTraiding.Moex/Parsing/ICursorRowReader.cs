using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    /// <summary>
    /// Читатель строк одного курсорного вида данных ALGOPACK: привязка типа строки к
    /// методу чтения строк. Все члены статические и разрешаются на этапе
    /// компиляции: общий каркас разбора, закрытый структурой-читателем, при нативной
    /// компиляции мономорфизируется — вызовы прямые, без делегатов, отражения
    /// и виртуальности.
    /// </summary>
    /// <typeparam name="TRow">Тип объекта передачи данных одной строки источника.</typeparam>
    public interface ICursorRowReader<TRow>
    {
        /// <summary>
        /// Чтение всех строк секции data в список по переданной схеме. Читатель схему
        /// не выбирает: единственный владелец схемы вида — паспорт IAlgCursorKind.
        /// Привязка к методу чтения — статическая переадресация, а не делегат:
        /// цель вызова известна на этапе компиляции.
        /// </summary>
        static abstract void ReadRows(
            ref Utf8JsonReader reader,
            List<TRow> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema);
    }
}

using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    /// <summary>
    /// Читатель строк одного курсорного вида данных ALGOPACK: ожидаемая схема колонок и
    /// привязка к методу чтения строк. Все члены статические и разрешаются на этапе
    /// компиляции: общий каркас разбора, закрытый структурой-читателем, при нативной
    /// компиляции мономорфизируется — вызовы прямые, без делегатов, отражения
    /// и виртуальности.
    /// </summary>
    /// <typeparam name="TRow">Тип объекта передачи данных одной строки источника.</typeparam>
    public interface ICursorRowReader<TRow>
    {
        /// <summary>Ожидаемая схема колонок вида; задаёт корневой блок и проверку columns.</summary>
        static abstract ColumnAndNumbersForParsing.ExpectedSchema Schema { get; }

        /// <summary>
        /// Чтение всех строк секции data в список. Привязка к методу чтения —
        /// статическая переадресация, а не делегат: цель вызова известна на этапе компиляции.
        /// </summary>
        static abstract void ReadRows(
            ref Utf8JsonReader reader,
            List<TRow> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema);
    }
}

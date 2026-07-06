using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Parsing;

namespace ProjectTraiding.Moex.Clients
{
    /// <summary>
    /// Паспорт разбора одного курсорного вида данных ALGOPACK: метки телеметрии и сырого
    /// снимка, ожидаемая схема колонок и привязка к разборщику. Все члены статические и
    /// разрешаются на этапе компиляции: обобщённый метод клиента, закрытый структурой-паспортом,
    /// при нативной компиляции мономорфизируется — вызовы прямые, без делегатов, отражения
    /// и виртуальности.
    /// </summary>
    /// <typeparam name="TRow">Тип объекта передачи данных одной строки источника.</typeparam>
    public interface IAlgCursorKind<TRow>
    {
        /// <summary>Метка вида данных для телеметрии и ключей сырого снимка (RawCaptureDataTypes).</summary>
        static abstract string CaptureDataType { get; }

        /// <summary>Метка рынка для телеметрии и ключей сырого снимка (RawCaptureMarkets).</summary>
        static abstract string CaptureMarket { get; }

        /// <summary>Ожидаемая схема колонок; одновременно источник параметра data.columns.</summary>
        static abstract ColumnAndNumbersForParsing.ExpectedSchema Schema { get; }

        /// <summary>
        /// Привязка к разборщику вида: статический метод-переадресация, а не делегат —
        /// цель вызова известна на этапе компиляции.
        /// </summary>
        static abstract List<TRow> Parse(ReadOnlySpan<byte> body, out PaginationCursorDTO cursor);
    }
}

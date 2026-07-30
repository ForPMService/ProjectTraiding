using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Parsing;

namespace ProjectTraiding.Moex.Clients
{
    /// <summary>
    /// Полный паспорт одного курсорного вида данных ALGOPACK: признаки задачи, адрес,
    /// метки телеметрии, ожидаемая схема колонок и привязка к разборщику. Все члены
    /// статические и разрешаются на этапе компиляции: обобщённые клиент и обработчик,
    /// закрытые структурой-паспортом, при нативной компиляции мономорфизируются — вызовы
    /// прямые, без делегатов, отражения и виртуальности.
    /// </summary>
    /// <typeparam name="TRow">Тип объекта передачи данных одной строки источника.</typeparam>
    public interface IAlgCursorKind<TRow>
    {
        /// <summary>Вид данных задачи (moex_load_tasks.data_kind), например "tradestats".
        /// Это ключ сопоставления с задачей оператора, а не метка телеметрии: у оповещений
        /// здесь "mega_alerts", тогда как в телеметрию уходит "alerts".</summary>
        static abstract string DataKind { get; }

        /// <summary>Рынок задачи (moex_load_tasks.market): "stock" или "futures".</summary>
        static abstract string Market { get; }

        /// <summary>Относительный адрес точки Московской биржи для инструмента.</summary>
        static abstract string BuildMethod(string secid);

        /// <summary>Метка вида данных для телеметрии. Отдельное понятие от DataKind.</summary>
        static abstract string TelemetryDataKind { get; }

        /// <summary>Метка рынка для телеметрии.</summary>
        static abstract string TelemetryMarket { get; }

        /// <summary>Ожидаемая схема колонок; одновременно источник параметра data.columns.</summary>
        static abstract ColumnAndNumbersForParsing.ExpectedSchema Schema { get; }

        /// <summary>Привязка к разборщику вида: статическая переадресация, а не делегат.</summary>
        static abstract List<TRow> Parse(ReadOnlySpan<byte> body, out PaginationCursorDTO cursor);
    }
}

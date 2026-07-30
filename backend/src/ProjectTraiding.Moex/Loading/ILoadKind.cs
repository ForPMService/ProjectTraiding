using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Pagination;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Паспорт вида данных с однотипной формой загрузки «диапазон дат → поток страниц»:
    /// строковые признаки для сопоставления с задачей, шаблон адреса и статическая привязка
    /// к методу клиента. Все члены статические и разрешаются на этапе компиляции: обобщённый
    /// обработчик, закрытый структурой-паспортом, при нативной компиляции мономорфизируется —
    /// вызовы прямые, без делегатов, отражения и виртуальности.
    /// </summary>
    /// <typeparam name="TRow">Тип объекта передачи данных одной строки источника.</typeparam>
    public interface ILoadKind<TRow>
    {
        /// <summary>Вид данных задачи (moex_load_tasks.data_kind), например "tradestats".</summary>
        static abstract string DataKind { get; }

        /// <summary>Рынок задачи (moex_load_tasks.market): "stock" или "futures".</summary>
        static abstract string Market { get; }

        /// <summary>Относительный адрес точки Московской биржи для инструмента.</summary>
        static abstract string BuildMethod(string secid);

        /// <summary>
        /// Привязка к конкретному методу клиента: поток страниц данного вида. Статический
        /// метод-переадресация, а не делегат: цель вызова известна на этапе компиляции.
        /// </summary>
        static abstract IAsyncEnumerable<List<TRow>> GetPages(
            MoexHttpAlgClient client,
            string method,
            Dictionary<string, string> query,
            LoadStopOutcome stopOutcome,
            CancellationToken ct);
    }
}

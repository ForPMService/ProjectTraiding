using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Loading
{
    // <summary>
    /// Обработчик одного вида данных: проверяет, его ли это задача, строит адрес и параметры
    /// запроса, вызывает свой метод клиента и пишет поток своим писателем. Тип строки источника
    /// инкапсулирован внутри; наружу отдаётся нетипизированный итог записи. Координатор выбирает
    /// обработчик через диспетчер и не знает, какой вид за ним стоит.
    /// </summary>
    public interface ILoadHandler
    {
        /// <summary>Подходит ли обработчик задаче (по виду данных, рынку и, для свечей, интервалу).</summary>
        bool CanHandle(MoexLoadTask task);

        /// <summary>
        /// Тянет данные вида и пишет их в ClickHouse. stopOutcome заполняется методом клиента
        /// в точке остановки; координатор читает его после. Возвращает итог записи.
        /// </summary>
        Task<RowWriteSummary> LoadAsync(MoexLoadTask task, LoadStopOutcome stopOutcome, CancellationToken ct);
    }
}

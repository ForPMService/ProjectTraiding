namespace History_DataMoex.Contracts.Dto
{
    /// <summary>
    /// Cursor-формат пагинации MOEX.
    /// Используется, когда источник возвращает служебные поля INDEX, TOTAL и PAGESIZE.
    /// См. MoexPaginationKind.Cursor.
    /// </summary>
    public record PaginationCursorDTO
    {
        /// <summary>
        /// Начальный индекс текущей страницы.
        /// 
        /// MOEX столбец: INDEX
        /// 
        /// Пример:
        /// 0, 100, 200.
        /// </summary>
        public int? Index { get; init; }

        /// <summary>
        /// Общее количество строк в ответе по запросу.
        /// 
        /// MOEX столбец: TOTAL
        /// 
        /// Если Total больше PageSize, нужно догружать следующие страницы.
        /// </summary>
        public int? Total { get; init; }

        /// <summary>
        /// Размер страницы.
        /// 
        /// MOEX столбец: PAGESIZE
        /// 
        /// Сколько строк MOEX отдаёт за один запрос.
        /// </summary>
        public int? PageSize { get; init; }
    }
}
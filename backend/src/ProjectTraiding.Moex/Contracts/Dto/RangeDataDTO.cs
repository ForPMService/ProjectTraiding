namespace History_DataMoex.Contracts.Dto
{
    /// <summary>
    /// Диапазон дат, который возвращает MOEX в служебном блоке dates.
    /// Это не пагинация и не торговая строка.
    /// Для пагинации см. MoexPaginationKind, MoexPageRequest и MoexPageResult.
    /// </summary>
    public record RangeDataDTO
    {
        /// <summary>
        /// Начальная дата доступного диапазона.
        /// 
        /// MOEX столбец: from
        /// </summary>
        public string? From { get; init; }

        /// <summary>
        /// Конечная дата доступного диапазона.
        /// 
        /// MOEX столбец: till
        /// </summary>
        public string? Till { get; init; }
    }
}
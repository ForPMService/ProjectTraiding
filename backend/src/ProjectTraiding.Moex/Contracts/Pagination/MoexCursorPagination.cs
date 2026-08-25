using ProjectTraiding.Moex.Contracts.Dto;

namespace ProjectTraiding.Moex.Contracts.Pagination;

/// <summary>
/// Единый helper решения для cursor-пагинации MOEX ALGOPACK / Calendar.
/// Заменяет 11 копий nullable-арифметики в клиентах.
/// </summary>
public static class MoexCursorPagination
{
    /// <summary>
    /// Определяет следующий шаг пагинации на основе cursor от MOEX.
    /// </summary>
    /// <param name="cursor">Cursor из ответа MOEX (Index, Total, PageSize — nullable).</param>
    /// <param name="pagesElapsed">Сколько страниц уже загружено в текущем цикле.</param>
    /// <param name="maxPagesGuard">Максимально допустимое число страниц (safety cap).</param>
    public static PaginationStep Next(
        PaginationCursorDTO cursor,
        int pagesElapsed,
        int maxPagesGuard)
    {
        if (cursor.Index is null || cursor.PageSize is null || cursor.Total is null)
            throw new InvalidOperationException(
                "Источник не вернул курсор целиком (INDEX, TOTAL, PAGESIZE): " +
                "полнота диапазона не доказана.");

        int index = cursor.Index.Value;
        int total = cursor.Total.Value;
        int pageSize = cursor.PageSize.Value;

        if (index < 0 || total < 0)
            throw new InvalidOperationException(
                $"Курсор источника отрицателен: INDEX={index}, TOTAL={total}.");

        if (pageSize <= 0)
            throw new InvalidOperationException(
                $"Размер страницы источника не положителен: PAGESIZE={pageSize}.");

        if (index > total)
            throw new InvalidOperationException(
                $"Курсор указывает за пределы набора: INDEX={index}, TOTAL={total}.");

        long next = (long)index + pageSize;

        if (next >= total)
            return PaginationStep.Stop("range_exhausted");

        if (pagesElapsed >= maxPagesGuard)
            throw new InvalidOperationException(
                $"Достигнут защитный предел Moex:MaxPagesPerLoad={maxPagesGuard} " +
                "при неисчерпанном диапазоне.");

        return PaginationStep.Continue((int)next);
    }
}

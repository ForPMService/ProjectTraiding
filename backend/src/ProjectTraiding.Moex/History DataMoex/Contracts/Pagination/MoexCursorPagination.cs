using History_DataMoex.Contracts.Dto;

namespace History_DataMoex.Contracts.Pagination;

/// <summary>
/// Единый helper решения для cursor-пагинации MOEX ALGOPACK / Calendar.
/// Заменяет 11 копий nullable-арифметики в клиентах.
/// </summary>
public static class MoexCursorPagination
{
    /// <summary>
    /// Определяет следующий шаг пагинации на основе cursor от MOEX.
    /// 
    /// Три причины остановки:
    ///   "empty_cursor"    — cursor пустой (Index/PageSize/Total is null)
    ///   "range_exhausted" — Index + PageSize >= Total, все данные получены
    ///   "safety_cap_hit"  — превышен лимит страниц (защита от бесконечного цикла)
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
            return PaginationStep.Stop("empty_cursor");

        int next = cursor.Index.Value + cursor.PageSize.Value;

        if (next >= cursor.Total.Value)
            return PaginationStep.Stop("range_exhausted");

        if (pagesElapsed >= maxPagesGuard)
            return PaginationStep.Stop("safety_cap_hit");

        return PaginationStep.Continue(next);
    }
}

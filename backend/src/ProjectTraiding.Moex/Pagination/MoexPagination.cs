using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto;

namespace ProjectTraiding.Moex.Pagination;

internal static class MoexPagination
{
    internal static async Task ReadCursorPagesAsync(
        Func<int, CancellationToken, Task<PaginationCursorDTO>> readPageAsync,
        ILogger logger,
        string endpoint,
        int maxPages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readPageAsync);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);

        int start = 0;
        int pagesRead = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PaginationCursorDTO cursor =
                await readPageAsync(start, cancellationToken);

            pagesRead++;

            if (cursor.Index is not int index ||
                cursor.Total is not int total ||
                cursor.PageSize is not int pageSize)
            {
                MoexLogMessages.CursorPaginationInvalid(
                    logger, endpoint, "missing_cursor_fields",
                    start, cursor.Index, cursor.Total, cursor.PageSize);

                throw new InvalidOperationException(
                    "MOEX не вернул полный курсор: INDEX, TOTAL, PAGESIZE.");
            }

            if (index < 0 || total < 0 ||
                pageSize <= 0 || index > total)
            {
                MoexLogMessages.CursorPaginationInvalid(
                    logger, endpoint, "invalid_cursor_values",
                    start, index, total, pageSize);

                throw new InvalidOperationException(
                    $"Некорректный курсор: INDEX={index}, " +
                    $"TOTAL={total}, PAGESIZE={pageSize}.");
            }

            long nextStart = (long)index + pageSize;

            if (nextStart >= total)
                return;

            if (nextStart <= start)
            {
                MoexLogMessages.CursorPaginationInvalid(
                    logger, endpoint, "cursor_not_advancing",
                    start, index, total, pageSize);

                throw new InvalidOperationException(
                    "Курсор MOEX не продвигается.");
            }

            if (pagesRead >= maxPages)
            {
                MoexLogMessages.PaginationPageLimitExceeded(
                    logger, endpoint, "cursor", pagesRead, maxPages);

                throw new InvalidOperationException(
                    $"Достигнут предел страниц: {maxPages}.");
            }

            start = checked((int)nextStart);
        }
    }

    internal static async Task ReadFixedPagesAsync(
        Func<int, CancellationToken, Task<int>> readPageAsync,
        ILogger logger,
        string endpoint,
        int pageSize,
        int maxPages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readPageAsync);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);

        int start = 0;
        int pagesRead = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int sourceRows = await readPageAsync(start, cancellationToken);
            pagesRead++;

            if (sourceRows < 0 || sourceRows > pageSize)
            {
                MoexLogMessages.FixedPagePaginationInvalid(
                    logger, endpoint, "invalid_source_rows",
                    start, sourceRows, pageSize);

                throw new InvalidOperationException(
                    $"Некорректное число строк страницы: {sourceRows}.");
            }

            if (sourceRows < pageSize)
                return;

            if (pagesRead >= maxPages)
            {
                MoexLogMessages.PaginationPageLimitExceeded(
                    logger, endpoint, "fixed_page", pagesRead, maxPages);

                throw new InvalidOperationException(
                    $"Достигнут предел страниц: {maxPages}.");
            }

            if (start > int.MaxValue - pageSize)
            {
                MoexLogMessages.FixedPagePaginationInvalid(
                    logger, endpoint, "start_overflow",
                    start, sourceRows, pageSize);

                throw new InvalidOperationException(
                    "Смещение фиксированной страницы выходит за диапазон Int32.");
            }

            start += pageSize;
        }
    }

    internal static async Task ReadDaySplitPagesAsync(
        DateOnly from,
        DateOnly till,
        Func<DateOnly, CancellationToken, Task> readDayAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readDayAsync);

        if (from > till)
        {
            throw new ArgumentException(
                "Начальная дата больше конечной.");
        }

        DateOnly current = from;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await readDayAsync(current, cancellationToken);

            if (current == till)
                return;

            current = current.AddDays(1);
        }
    }
}

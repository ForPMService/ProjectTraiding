
using Microsoft.Extensions.Logging;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto;

namespace ProjectTraiding.Moex.Pagination;

internal static class MoexPagination
{
    internal static bool TryNextCursor(
        PaginationCursorDTO cursor,
        int currentStart,
        int pagesRead,
        int maxPages,
        ILogger logger,
        string endpoint,
        out int nextStart)
    {
        nextStart = default;

        if (cursor.Index is not int index ||
            cursor.Total is not int total ||
            cursor.PageSize is not int pageSize)
        {
            MoexLogMessages.CursorPaginationInvalid(
                logger, endpoint, "missing_cursor_fields",
                currentStart, cursor.Index,
                cursor.Total, cursor.PageSize);

            throw new InvalidOperationException();
        }

        if (index < 0 || total < 0 ||
            pageSize <= 0 || index > total)
        {
            MoexLogMessages.CursorPaginationInvalid(
                logger, endpoint, "invalid_cursor_values",
                currentStart, index, total, pageSize);

            throw new InvalidOperationException();
        }

        long next = (long)index + pageSize;

        if (next >= total)
            return false;

        if (next <= currentStart)
        {
            MoexLogMessages.CursorPaginationInvalid(
                logger, endpoint, "cursor_not_advancing",
                currentStart, index, total, pageSize);

            throw new InvalidOperationException();
        }

        if (pagesRead >= maxPages)
        {
            MoexLogMessages.PaginationPageLimitExceeded(
                logger, endpoint, "cursor",
                pagesRead, maxPages);

            throw new InvalidOperationException();
        }

        nextStart = checked((int)next);
        return true;
    }

    internal static bool TryNextFixedPage(
        int currentStart,
        int sourceRows,
        int pageSize,
        int pagesRead,
        int maxPages,
        ILogger logger,
        string endpoint,
        out int nextStart)
    {
        nextStart = default;

        if (pageSize <= 0 ||
            sourceRows < 0 ||
            sourceRows > pageSize)
        {
            MoexLogMessages.FixedPagePaginationInvalid(
                logger, endpoint, "invalid_source_rows",
                currentStart, sourceRows, pageSize);

            throw new InvalidOperationException();
        }

        if (sourceRows < pageSize)
            return false;

        if (pagesRead >= maxPages)
        {
            MoexLogMessages.PaginationPageLimitExceeded(
                logger, endpoint, "fixed_page",
                pagesRead, maxPages);

            throw new InvalidOperationException();
        }

        if (currentStart < 0 ||
            currentStart > int.MaxValue - pageSize)
        {
            MoexLogMessages.FixedPagePaginationInvalid(
                logger, endpoint, "start_overflow",
                currentStart, sourceRows, pageSize);

            throw new InvalidOperationException();
        }

        nextStart = currentStart + pageSize;
        return true;
    }

    internal static bool TryNextDay(
        DateOnly current,
        DateOnly till,
        out DateOnly nextDay)
    {
        nextDay = default;

        if (current > till)
            throw new ArgumentOutOfRangeException(nameof(current));

        if (current == till)
            return false;

        nextDay = current.AddDays(1);
        return true;
    }
}

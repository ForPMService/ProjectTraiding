using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Contracts.Pagination;

namespace TestHistoryData
{
    public class PaginationLogicTests
    {
        // ═══════════════════════════════════════════════════════════
        // 1. Continue — есть ещё страницы
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Next_MorePagesAhead_ReturnsContinueWithCorrectStart()
        {
            var cursor = new PaginationCursorDTO { Index = 0, Total = 5000, PageSize = 100 };

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 1, maxPagesGuard: 10000);

            Assert.False(step.IsStop);
            Assert.Equal(100, step.NextStart);
        }

        // ═══════════════════════════════════════════════════════════
        // 2. Stop — range exhausted (Index + PageSize >= Total)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Next_RangeExhausted_ReturnsStop()
        {
            var cursor = new PaginationCursorDTO { Index = 4900, Total = 5000, PageSize = 100 };

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 50, maxPagesGuard: 10000);

            Assert.True(step.IsStop);
        }

        // ═══════════════════════════════════════════════════════════
        // 3. Stop — range exhausted exact boundary (4950 + 50 == 5000)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Next_ExactBoundary_ReturnsStop()
        {
            var cursor = new PaginationCursorDTO { Index = 4950, Total = 5000, PageSize = 50 };

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 100, maxPagesGuard: 10000);

            Assert.True(step.IsStop);
        }

        // ═══════════════════════════════════════════════════════════
        // 4. Stop — safety cap hit (pagesElapsed >= maxPagesPerLoad)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Next_SafetyCapHit_ReturnsStop()
        {
            var cursor = new PaginationCursorDTO { Index = 0, Total = 999999, PageSize = 100 };

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 10, maxPagesGuard: 10);

            Assert.True(step.IsStop);
        }

        // ═══════════════════════════════════════════════════════════
        // 5. Stop — empty cursor (Index is null)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Next_EmptyCursor_ReturnsStop()
        {
            var cursor = new PaginationCursorDTO { Index = null, Total = null, PageSize = null };

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 1, maxPagesGuard: 10000);

            Assert.True(step.IsStop);
        }

        // ═══════════════════════════════════════════════════════════
        // 6. Stop — Total is null, Index not null
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Next_TotalNull_ReturnsStop()
        {
            var cursor = new PaginationCursorDTO { Index = 0, Total = null, PageSize = 100 };

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 1, maxPagesGuard: 10000);

            Assert.True(step.IsStop);
        }

        // ═══════════════════════════════════════════════════════════
        // 7. Continue — multiple pages elapsed, still have data
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Next_MultiplePagesElapsed_ReturnsContinue()
        {
            var cursor = new PaginationCursorDTO { Index = 500, Total = 5000, PageSize = 100 };

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 6, maxPagesGuard: 10000);

            Assert.False(step.IsStop);
            Assert.Equal(600, step.NextStart);
        }

        // ═══════════════════════════════════════════════════════════
        // 8. Stop — maxPagesPerLoad = 1 (один запрос)
        // ═══════════════════════════════════════════════════════════

        [Fact]
        public void Next_MaxPagesOne_StopsAfterFirst()
        {
            var cursor = new PaginationCursorDTO { Index = 0, Total = 5000, PageSize = 100 };

            PaginationStep step = MoexCursorPagination.Next(cursor, pagesElapsed: 1, maxPagesGuard: 1);

            Assert.True(step.IsStop);
        }
    }
}

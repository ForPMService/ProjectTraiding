namespace ProjectTraiding.Moex.Loading.Planning
{
    public readonly record struct CoverageInterval(DateOnly From, DateOnly Till);
    public readonly record struct MissingInterval(DateOnly From, DateOnly Till);
    public readonly record struct CoverageSubtractResult(
        IReadOnlyList<MissingInterval> Missing,
        int CoveredIntervalsCount,
        int MissingIntervalsCount,
        int MissingDaysTotal);

    public static class LoadedRangeCoverageCalculator
    {
        public static CoverageSubtractResult Subtract(
            DateOnly effectiveFrom,
            DateOnly effectiveTill,
            IReadOnlyList<CoverageInterval> covered)
        {
            if (effectiveFrom > effectiveTill)
                return new CoverageSubtractResult(Array.Empty<MissingInterval>(), 0, 0, 0);

            List<CoverageInterval> clipped = new();
            for (int i = 0; i < covered.Count; i++)
            {
                CoverageInterval interval = covered[i];
                DateOnly from = interval.From > effectiveFrom ? interval.From : effectiveFrom;
                DateOnly till = interval.Till < effectiveTill ? interval.Till : effectiveTill;
                if (from <= till)
                    clipped.Add(new CoverageInterval(from, till));
            }

            clipped.Sort(CoverageIntervalComparer.Instance);
            List<CoverageInterval> merged = new();
            for (int i = 0; i < clipped.Count; i++)
            {
                CoverageInterval current = clipped[i];
                if (merged.Count == 0)
                {
                    merged.Add(current);
                    continue;
                }

                CoverageInterval last = merged[^1];
                if (current.From <= last.Till.AddDays(1))
                {
                    DateOnly till = current.Till > last.Till ? current.Till : last.Till;
                    merged[^1] = new CoverageInterval(last.From, till);
                    continue;
                }

                merged.Add(current);
            }

            List<MissingInterval> missing = new();
            DateOnly cursor = effectiveFrom;
            for (int i = 0; i < merged.Count; i++)
            {
                CoverageInterval interval = merged[i];
                if (interval.From > cursor)
                    missing.Add(new MissingInterval(cursor, interval.From.AddDays(-1)));

                DateOnly nextCursor = interval.Till.AddDays(1);
                if (nextCursor > cursor)
                    cursor = nextCursor;
            }

            if (cursor <= effectiveTill)
                missing.Add(new MissingInterval(cursor, effectiveTill));

            int missingDaysTotal = 0;
            for (int i = 0; i < missing.Count; i++)
                missingDaysTotal += missing[i].Till.DayNumber - missing[i].From.DayNumber + 1;

            return new CoverageSubtractResult(missing, merged.Count, missing.Count, missingDaysTotal);
        }

        private sealed class CoverageIntervalComparer : IComparer<CoverageInterval>
        {
            public static readonly CoverageIntervalComparer Instance = new();
            public int Compare(CoverageInterval left, CoverageInterval right)
            {
                int fromComparison = left.From.CompareTo(right.From);
                return fromComparison != 0 ? fromComparison : left.Till.CompareTo(right.Till);
            }
        }
    }
}

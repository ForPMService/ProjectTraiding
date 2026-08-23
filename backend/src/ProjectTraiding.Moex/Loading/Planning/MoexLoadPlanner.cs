using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Loading.Planning
{
    public readonly record struct MoexLoadPlanInstrument(
        string Secid,
        string Market,
        string Boardid,
        DateOnly DateFrom,
        DateOnly DateTill);

    public sealed record MoexLoadPlanRequest(
        IReadOnlyList<MoexLoadPlanInstrument> Instruments,
        string[] StockDataKinds,
        string[] FuturesDataKinds,
        int[] CandleIntervals,
        string StorageTarget,
        int? SliceWeeks);

    public readonly record struct MoexLoadWindow(
        string Secid,
        string Market,
        string Boardid,
        string DataKind,
        int? CandleInterval,
        DateOnly DateFrom,
        DateOnly DateTill,
        string StorageTarget);

    public sealed record MoexLoadPlanResult(
        IReadOnlyList<MoexLoadWindow> Windows,
        int SkippedCoveredCount,
        int BlockedCount,
        int? EffectiveSliceWeeks);

    /// <summary>
    /// Планирует загрузку по правилам MOEX: зрелая дата, субъекты открытого интереса,
    /// покрытие и нарезка окон принадлежат владельцу данных, а не маршруту управления.
    /// </summary>
    public sealed class MoexLoadPlanner
    {
        private readonly FutoiSubjectReader _subjectReader;
        private readonly LoadedRangeCoverageReader _coverageReader;

        public MoexLoadPlanner(
            FutoiSubjectReader subjectReader,
            LoadedRangeCoverageReader coverageReader)
        {
            _subjectReader = subjectReader;
            _coverageReader = coverageReader;
        }

        public async Task<MoexLoadWindow?> PlanSingleAsync(MoexLoadWindow request, CancellationToken ct)
        {
            if (request.DataKind != "futoi")
                return request;

            Dictionary<string, string> subjects = await _subjectReader.ResolveAsync([request.Secid], ct);
            return subjects.TryGetValue(request.Secid, out string? subject)
                ? request with { Secid = subject }
                : null;
        }

        public async Task<MoexLoadPlanResult> PlanBulkAsync(MoexLoadPlanRequest request, CancellationToken ct)
        {
            DateOnly lastMatureDate = MoexTime.Today.AddDays(-1);
            bool needsFutoi = Contains(request.FuturesDataKinds, "futoi");
            Dictionary<string, string> subjects = needsFutoi
                ? await _subjectReader.ResolveAsync(GetFuturesSecids(request.Instruments), ct)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            List<MoexLoadWindow> windows = new();
            int skippedCoveredCount = 0;
            int blockedCount = 0;
            int? effectiveSliceWeeks = request.SliceWeeks.HasValue
                ? (request.SliceWeeks.Value < 1 ? 1 : request.SliceWeeks.Value)
                : null;

            for (int instrumentIndex = 0; instrumentIndex < request.Instruments.Count; instrumentIndex++)
            {
                MoexLoadPlanInstrument instrument = request.Instruments[instrumentIndex];
                string[] dataKinds = instrument.Market == "stock"
                    ? request.StockDataKinds : request.FuturesDataKinds;
                DateOnly effectiveTill = instrument.DateTill < lastMatureDate
                    ? instrument.DateTill : lastMatureDate;

                for (int intervalIndex = 0; intervalIndex < request.CandleIntervals.Length; intervalIndex++)
                {
                    skippedCoveredCount += await AddMissingWindowsAsync(
                        windows, instrument, "candles", request.CandleIntervals[intervalIndex],
                        request.StorageTarget, request.SliceWeeks, instrument.DateFrom, effectiveTill, ct);
                }

                for (int dataKindIndex = 0; dataKindIndex < dataKinds.Length; dataKindIndex++)
                {
                    string dataKind = dataKinds[dataKindIndex];
                    MoexLoadPlanInstrument subjectInstrument = instrument;
                    if (dataKind == "futoi")
                    {
                        if (!subjects.TryGetValue(instrument.Secid, out string? subject))
                        {
                            blockedCount++;
                            continue;
                        }
                        subjectInstrument = instrument with { Secid = subject };
                    }

                    skippedCoveredCount += await AddMissingWindowsAsync(
                        windows, subjectInstrument, dataKind, null, request.StorageTarget,
                        request.SliceWeeks, instrument.DateFrom, effectiveTill, ct);
                }
            }

            return new MoexLoadPlanResult(windows, skippedCoveredCount, blockedCount, effectiveSliceWeeks);
        }

        private async Task<int> AddMissingWindowsAsync(
            List<MoexLoadWindow> windows,
            MoexLoadPlanInstrument instrument,
            string dataKind,
            int? candleInterval,
            string storageTarget,
            int? requestedSliceWeeks,
            DateOnly effectiveFrom,
            DateOnly effectiveTill,
            CancellationToken ct)
        {
            if (effectiveFrom > effectiveTill)
                return 0;

            IReadOnlyList<CoverageInterval> covered = await _coverageReader.GetCoveredRangesAsync(
                instrument.Secid, instrument.Market, instrument.Boardid, dataKind, candleInterval,
                storageTarget, effectiveFrom, effectiveTill, ct);
            CoverageSubtractResult missing = LoadedRangeCoverageCalculator.Subtract(
                effectiveFrom, effectiveTill, covered);
            int sliceWeeks = LoadTaskBulkExpander.ResolveSliceWeeks(
                requestedSliceWeeks, dataKind, instrument.Market);
            LoadTaskBulkExpander.AddMissingWindows(
                windows, instrument, dataKind, candleInterval, storageTarget, sliceWeeks, missing.Missing);
            return missing.CoveredIntervalsCount;
        }

        private static bool Contains(string[] values, string value)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] == value)
                    return true;
            }
            return false;
        }

        private static string[] GetFuturesSecids(IReadOnlyList<MoexLoadPlanInstrument> instruments)
        {
            List<string> secids = new();
            for (int index = 0; index < instruments.Count; index++)
            {
                if (instruments[index].Market == "futures")
                    secids.Add(instruments[index].Secid);
            }
            return secids.ToArray();
        }
    }
}

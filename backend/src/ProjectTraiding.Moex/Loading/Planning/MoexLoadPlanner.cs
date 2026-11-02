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
        int? SliceWeeks);

    public readonly record struct MoexLoadWindow(
        string Secid,
        string Market,
        string Boardid,
        string DataKind,
        int? CandleInterval,
        DateOnly DateFrom,
        DateOnly DateTill);

    public sealed record MoexLoadPlanResult(
        IReadOnlyList<MoexLoadWindow> Windows,
        int? EffectiveSliceWeeks);

    /// <summary>
    /// Планирует загрузку по правилам MOEX: последняя допустимая дата, субъекты открытого интереса
    /// принадлежат владельцу данных, а не маршруту управления.
    /// </summary>
    public sealed class MoexLoadPlanner
    {
        private readonly FutoiSubjectReader _subjectReader;

        public MoexLoadPlanner(FutoiSubjectReader subjectReader)
        {
            _subjectReader = subjectReader;
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
            DateOnly lastAllowedDate = MoexTime.Today;
            bool needsFutoi = Contains(request.FuturesDataKinds, "futoi");
            Dictionary<string, string> subjects = needsFutoi
                ? await _subjectReader.ResolveAsync(GetFuturesSecids(request.Instruments), ct)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            List<MoexLoadWindow> windows = new();

            for (int instrumentIndex = 0; instrumentIndex < request.Instruments.Count; instrumentIndex++)
            {
                MoexLoadPlanInstrument instrument = request.Instruments[instrumentIndex];
                string[] dataKinds = instrument.Market == "stock"
                    ? request.StockDataKinds : request.FuturesDataKinds;
                DateOnly effectiveTill = instrument.DateTill < lastAllowedDate
                    ? instrument.DateTill : lastAllowedDate;

                for (int intervalIndex = 0; intervalIndex < request.CandleIntervals.Length; intervalIndex++)
                {
                    AddWindows(
                        windows, instrument, "candles", request.CandleIntervals[intervalIndex],
                        instrument.DateFrom, effectiveTill);
                }

                for (int dataKindIndex = 0; dataKindIndex < dataKinds.Length; dataKindIndex++)
                {
                    string dataKind = dataKinds[dataKindIndex];
                    MoexLoadPlanInstrument subjectInstrument = instrument;
                    if (dataKind == "futoi")
                    {
                        if (!subjects.TryGetValue(instrument.Secid, out string? subject))
                            continue;
                        subjectInstrument = instrument with { Secid = subject };
                    }

                    AddWindows(
                        windows, subjectInstrument, dataKind, null,
                        instrument.DateFrom, effectiveTill);
                }
            }

            return new MoexLoadPlanResult(windows, null);
        }

        private static void AddWindows(
            List<MoexLoadWindow> windows,
            MoexLoadPlanInstrument instrument,
            string dataKind,
            int? candleInterval,
            DateOnly effectiveFrom,
            DateOnly effectiveTill)
        {
            if (effectiveFrom > effectiveTill)
                return;

            windows.Add(new MoexLoadWindow(
                instrument.Secid, instrument.Market, instrument.Boardid, dataKind,
                candleInterval, effectiveFrom, effectiveTill));
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

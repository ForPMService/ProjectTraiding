using ProjectTraiding.Management.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Expansion
{
    public static class LoadTaskBulkExpander
    {
        public static IReadOnlyList<LoadTaskCreateRequest> Expand(LoadTaskBulkCreateRequest request)
        {
            List<LoadTaskCreateRequest> tasks = new();

            for (int instrumentIndex = 0; instrumentIndex < request.Instruments.Count; instrumentIndex++)
            {
                LoadTaskBulkInstrumentRequest instrument = request.Instruments[instrumentIndex];
                string[] dataKinds = instrument.Market == "stock"
                    ? request.StockDataKinds
                    : request.FuturesDataKinds;

                for (int intervalIndex = 0; intervalIndex < request.CandleIntervals.Length; intervalIndex++)
                {
                    int candleWeeks = ResolveSliceWeeks(request.SliceWeeks, "candles", instrument.Market);
                    AddWindows(
                        tasks,
                        instrument,
                        dataKind: "candles",
                        candleInterval: request.CandleIntervals[intervalIndex],
                        storageTarget: request.StorageTarget,
                        sliceDays: candleWeeks * 7);
                }

                for (int dataKindIndex = 0; dataKindIndex < dataKinds.Length; dataKindIndex++)
                {
                    string dataKind = dataKinds[dataKindIndex];
                    int weeks = ResolveSliceWeeks(request.SliceWeeks, dataKind, instrument.Market);
                    AddWindows(
                        tasks,
                        instrument,
                        dataKind: dataKind,
                        candleInterval: null,
                        storageTarget: request.StorageTarget,
                        sliceDays: weeks * 7);
                }
            }

            return tasks;
        }

        /// <summary>
        /// Ширина окна нарезки в неделях для конкретной пары «вид данных × рынок».
        /// Если оператор задал ширину явно — используется она (не меньше одной недели) для всех
        /// видов. Если не задал (значение отсутствует) — применяется правило по умолчанию:
        /// узкое недельное окно для медленной статистики заявок по акциям и широкое годовое
        /// окно для остальных видов. Инструмент правилом не различается — только вид и рынок.
        /// </summary>
        private static int ResolveSliceWeeks(int? requestedWeeks, string dataKind, string market)
        {
            if (requestedWeeks.HasValue)
                return requestedWeeks.Value < 1 ? 1 : requestedWeeks.Value;

            return GetDefaultSliceWeeks(dataKind, market);
        }

        private static int GetDefaultSliceWeeks(string dataKind, string market)
        {
            if (dataKind == "orderstats" && market == "stock")
                return 1;

            return 52;
        }

        private static void AddWindows(
            List<LoadTaskCreateRequest> tasks,
            LoadTaskBulkInstrumentRequest instrument,
            string dataKind,
            int? candleInterval,
            string storageTarget,
            int sliceDays)
        {
            DateOnly windowFrom = instrument.DateFrom;

            while (windowFrom <= instrument.DateTill)
            {
                DateOnly windowTill = windowFrom.AddDays(sliceDays - 1);
                if (windowTill > instrument.DateTill)
                    windowTill = instrument.DateTill;

                tasks.Add(new LoadTaskCreateRequest(
                    Secid: instrument.Secid,
                    Market: instrument.Market,
                    Boardid: instrument.Boardid,
                    DataKind: dataKind,
                    CandleInterval: candleInterval,
                    DateFrom: windowFrom,
                    DateTill: windowTill,
                    StorageTarget: storageTarget));

                windowFrom = windowFrom.AddDays(sliceDays);
            }
        }
    }
}

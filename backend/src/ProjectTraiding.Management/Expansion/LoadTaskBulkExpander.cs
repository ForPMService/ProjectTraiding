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
            int normalizedSliceWeeks = (request.SliceWeeks < 1) ? 1 : request.SliceWeeks;
            int sliceDays = normalizedSliceWeeks * 7;
            List<LoadTaskCreateRequest> tasks = new();

            for (int instrumentIndex = 0; instrumentIndex < request.Instruments.Count; instrumentIndex++)
            {
                LoadTaskBulkInstrumentRequest instrument = request.Instruments[instrumentIndex];
                string[] dataKinds = instrument.Market == "stock"
                    ? request.StockDataKinds
                    : request.FuturesDataKinds;

                for (int intervalIndex = 0; intervalIndex < request.CandleIntervals.Length; intervalIndex++)
                {
                    AddWindows(
                        tasks,
                        instrument,
                        dataKind: "candles",
                        candleInterval: request.CandleIntervals[intervalIndex],
                        storageTarget: request.StorageTarget,
                        sliceDays: sliceDays);
                }

                for (int dataKindIndex = 0; dataKindIndex < dataKinds.Length; dataKindIndex++)
                {
                    AddWindows(
                        tasks,
                        instrument,
                        dataKind: dataKinds[dataKindIndex],
                        candleInterval: null,
                        storageTarget: request.StorageTarget,
                        sliceDays: sliceDays);
                }
            }

            return tasks;
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

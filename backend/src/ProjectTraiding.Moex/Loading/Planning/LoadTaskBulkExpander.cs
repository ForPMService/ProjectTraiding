namespace ProjectTraiding.Moex.Loading.Planning
{
    public static class LoadTaskBulkExpander
    {
        public static int ResolveSliceWeeks(int? requestedWeeks, string dataKind, string market)
        {
            if (requestedWeeks.HasValue)
                return requestedWeeks.Value < 1 ? 1 : requestedWeeks.Value;

            return dataKind == "orderstats" && market == "stock" ? 1 : 52;
        }

        public static int AddMissingWindows(
            List<MoexLoadWindow> windows,
            MoexLoadPlanInstrument instrument,
            string dataKind,
            int? candleInterval,
            string storageTarget,
            int sliceWeeks,
            IReadOnlyList<MissingInterval> missing)
        {
            int initialCount = windows.Count;
            int sliceDays = sliceWeeks * 7;
            for (int i = 0; i < missing.Count; i++)
            {
                DateOnly windowFrom = missing[i].From;
                while (windowFrom <= missing[i].Till)
                {
                    DateOnly windowTill = windowFrom.AddDays(sliceDays - 1);
                    if (windowTill > missing[i].Till)
                        windowTill = missing[i].Till;

                    windows.Add(new MoexLoadWindow(
                        instrument.Secid,
                        instrument.Market,
                        instrument.Boardid,
                        dataKind,
                        candleInterval,
                        windowFrom,
                        windowTill,
                        storageTarget));
                    windowFrom = windowFrom.AddDays(sliceDays);
                }
            }

            return windows.Count - initialCount;
        }
    }
}

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

        public static void AddWindows(
            List<MoexLoadWindow> windows,
            MoexLoadPlanInstrument instrument,
            string dataKind,
            int? candleInterval,
            int sliceWeeks,
            DateOnly from,
            DateOnly till)
        {
            int sliceDays = sliceWeeks * 7;
            DateOnly windowFrom = from;
            while (windowFrom <= till)
            {
                DateOnly windowTill = windowFrom.AddDays(sliceDays - 1);
                if (windowTill > till)
                    windowTill = till;

                windows.Add(new MoexLoadWindow(
                    instrument.Secid,
                    instrument.Market,
                    instrument.Boardid,
                    dataKind,
                    candleInterval,
                    windowFrom,
                    windowTill));
                windowFrom = windowFrom.AddDays(sliceDays);
            }
        }
    }
}

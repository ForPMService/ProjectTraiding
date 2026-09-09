namespace ProjectTraiding.CustomFeatures.Contracts
{
    /// <summary>
    /// День календаря ставки. Признаки RatePresent, RoisfixPresent, RuoniaPresent и
    /// ExpertPresent означают, что блок передан оператором и перезаписывается целиком.
    /// Непереданный блок при повторной записи дня остаётся прежним.
    /// </summary>
    public readonly record struct CbRateCalendarDayUpsertCommand(
        DateOnly D,
        bool RatePresent,
        decimal? RateAnnounced,
        decimal? RateEffective,
        bool RoisfixPresent,
        decimal? Roisfix1w,
        decimal? Roisfix2w,
        decimal? Roisfix1m,
        decimal? Roisfix2m,
        decimal? Roisfix3m,
        decimal? Roisfix6m,
        decimal? Roisfix1y,
        decimal? Roisfix2y,
        DateTime? RoisfixKnownAt,
        bool RuoniaPresent,
        decimal? Ruonia,
        string? RuoniaStatus,
        DateTime? RuoniaKnownAt,
        bool ExpertPresent,
        decimal? ExpertRate,
        string? ExpertSource,
        DateTime? ExpertKnownAt);
}

namespace ProjectTraiding.CustomFeatures.Contracts
{
    /// <summary>
    /// Строка журнала ставки. Пустой CycleId означает новый цикл: идентификатор
    /// выдаёт писатель. IsCancelled относится к этой строке и закрывает цикл.
    /// </summary>
    public readonly record struct CbRateMeetingFactCreateCommand(
        Guid? CycleId,
        string FactType,
        DateOnly MeetingDate,
        DateTime KnownAt,
        DateTime? ScheduledPublicationAt,
        DateTime? PublishedAt,
        decimal? RateBefore,
        decimal? RateAfter,
        DateOnly? EffectiveFrom,
        bool IsCancelled);
}
